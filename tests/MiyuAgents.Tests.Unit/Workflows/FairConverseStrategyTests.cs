using MiyuAgents.Core;
using MiyuAgents.Core.Events;
using MiyuAgents.Workflows;
using Xunit;

namespace MiyuAgents.Tests.Unit.Workflows;

// Fake polleable: contesta el "¿querés hablar?" según <paramref name="wants"/>. La estrategia sólo llama
// WantsTurnAsync — ProcessAsync/eventos no se usan.
#pragma warning disable CS0067
file sealed class PollBidder(string id, bool wants) : INodeAgent, IBiddingParticipant
{
    public string AgentId => id;
    public string AgentName => id;
    public AgentRole Role => AgentRole.Conversation;
    public Task<bool> WantsTurnAsync(NodeState state, CancellationToken ct = default) => Task.FromResult(wants);
    public Task<NodeResult> RunNodeAsync(NodeState state, CancellationToken ct = default)
        => Task.FromResult(NodeResult.From(new AgentResponse { AgentId = id, AgentName = id, Role = Role }, NodeSignal.Done));
    public Task<AgentResponse> ProcessAsync(AgentContext ctx, CancellationToken ct = default) => throw new NotSupportedException();
    public event AsyncEventHandler<MessageReceivedEventArgs>?       OnMessageReceived;
    public event AsyncEventHandler<LlmCallRequestedEventArgs>?      OnLLMCallRequested;
    public event AsyncEventHandler<LlmCallRespondedEventArgs>?      OnLLMCallResponded;
    public event AsyncEventHandler<AgentResponseProducedEventArgs>? OnResponseProduced;
    public event AsyncEventHandler<AgentErrorEventArgs>?            OnError;
}
#pragma warning restore CS0067

// La política de turnos por EQUIDAD (idea de Miyu): base = el que menos participó (desempate aleatorio),
// + request-turn (bids) acotados para que 2-3 no acaparen dejando al resto afuera.
public class FairConverseStrategyTests
{
    static NodeResult Turn(string id) => NodeResult.From(
        new AgentResponse { AgentId = id, AgentName = id, Role = AgentRole.Conversation }, NodeSignal.Done);

    static NodeState State(IEnumerable<NodeResult> history, IEnumerable<Bid>? bids = null, int round = 0)
        => new() { Input = "seed", History = history.ToList(), Bids = (bids ?? []).ToList(), Round = round };

    static readonly string[] Roster = ["a", "b", "c", "d"];

    [Fact]
    public async Task Base_PicksLeastParticipant()
    {
        // a=1, b=1, c=0, d=1 → menos participó c. lastSpeaker=d (no lo excluye del cálculo, sí de candidatos).
        var strat = new FairConverseStrategy(Roster, rng: new Random(1));
        var d = await strat.NextAsync(State([Turn("a"), Turn("b"), Turn("d")]));
        Assert.Equal("c", d.RunNext.Single());
    }

    [Fact]
    public async Task Base_ExcludesLastSpeaker_AntiPingPong()
    {
        // Roster de 3: c es el de MENOS participación PERO acaba de hablar → NO se elige (anti-ping-pong).
        // a=2, b=2, c=1; lastSpeaker=c. Candidatos = {a,b} (empatados) → nunca c.
        string[] roster3 = ["a", "b", "c"];
        var strat = new FairConverseStrategy(roster3, rng: new Random(1));
        var d = await strat.NextAsync(State([Turn("a"), Turn("b"), Turn("a"), Turn("b"), Turn("c")]));
        Assert.NotEqual("c", d.RunNext.Single());
        Assert.Contains(d.RunNext.Single(), new[] { "a", "b" });
    }

    [Fact]
    public async Task OpeningSpeaker_MentionOpensTurn_OverridesEquity()
    {
        // MENCIÓN: aunque 'a' domine (a=3) y la equidad elegiría a otro, si el humano dirigió el mensaje a 'a',
        // 'a' ABRE el turno (Round 0). Override de la equidad, sólo en la apertura del run.
        var strat = new FairConverseStrategy(Roster, rng: new Random(1), openingSpeaker: "a");
        var d = await strat.NextAsync(State([Turn("a"), Turn("a"), Turn("a")], round: 0));
        Assert.Equal("a", d.RunNext.Single());
    }

    [Fact]
    public async Task OpeningSpeaker_OnlyAtRoundZero()
    {
        // El opening speaker sólo aplica en la apertura (Round 0). En Round > 0 vuelve la equidad normal
        // (a domina y acaba de hablar → no se elige).
        var strat = new FairConverseStrategy(Roster, rng: new Random(1), openingSpeaker: "a");
        var d = await strat.NextAsync(State([Turn("a"), Turn("a"), Turn("a")], round: 1));
        Assert.NotEqual("a", d.RunNext.Single());
    }

    [Fact]
    public async Task Bid_HonoredWhenNotDominating()
    {
        // Todos en 0; c pide turno → gana sobre la base aleatoria (bid elegible: 0 ≤ min+slack, no fue último).
        var strat = new FairConverseStrategy(Roster, rng: new Random(1));
        var d = await strat.NextAsync(State([], bids: [new Bid("c")]));
        Assert.Equal("c", d.RunNext.Single());
    }

    [Fact]
    public async Task Bid_IgnoredWhenBidderDominates_LoopPrevention()
    {
        // A↔B loopearon (a=2, b=2), c y d en 0. lastSpeaker=b. A pide turno para SEGUIR el loop.
        // Como A ya domina (2 > min(0)+slack(1)), su bid NO es elegible → la base tira desde los starved
        // (c/d) — se rompe el loop y entra el que estaba afuera.
        var strat = new FairConverseStrategy(Roster, bidFairnessSlack: 1, rng: new Random(1));
        var d = await strat.NextAsync(State([Turn("a"), Turn("b"), Turn("a"), Turn("b")], bids: [new Bid("a")]));
        Assert.Contains(d.RunNext.Single(), new[] { "c", "d" });   // NO 'a' ni 'b'
    }

    [Fact]
    public async Task Bid_AmongEligible_PicksLeastParticipant()
    {
        // Bids de b (1) y c (0), ambos elegibles → gana el de MENOS participación (c).
        var strat = new FairConverseStrategy(Roster, rng: new Random(1));
        var d = await strat.NextAsync(State([Turn("a"), Turn("b"), Turn("d")], bids: [new Bid("b"), new Bid("c")]));
        Assert.Equal("c", d.RunNext.Single());
    }

    [Fact]
    public async Task StopsAtMaxRounds()
    {
        var strat = new FairConverseStrategy(Roster, maxRounds: 3);
        var d = await strat.NextAsync(State([Turn("a"), Turn("b"), Turn("c")], round: 3));
        Assert.True(d.IsTerminal);
    }

    [Fact]
    public async Task Recency_WindowIgnoresOldDomination()
    {
        // 'a' dominó AL PRINCIPIO (a,a,a) pero la ventana reciente sólo ve [b,c] → a luce sub-participativo
        // y entra. Sin ventana, a=3 y no lo elegiría. lastSpeaker=c → candidatos {a,b}, a=0 en la ventana.
        var strat = new FairConverseStrategy(["a", "b", "c"], recencyWindow: 2, rng: new Random(1));
        var d = await strat.NextAsync(State([Turn("a"), Turn("a"), Turn("a"), Turn("b"), Turn("c")]));
        Assert.Equal("a", d.RunNext.Single());
    }

    [Fact]
    public async Task PersonaWeights_TalkativeCharToleratesMore()
    {
        // a=1, c=1 (empate crudo) PERO 'a' es habladora (peso 2) → eff(a)=0.5 < eff(c)=1 → gana 'a'.
        // lastSpeaker=b → candidatos {a,c}.
        var weights = new Dictionary<string, double> { ["a"] = 2.0 };
        var strat = new FairConverseStrategy(["a", "b", "c"], weights: weights, rng: new Random(1));
        var d = await strat.NextAsync(State([Turn("a"), Turn("b"), Turn("c"), Turn("b")]));
        Assert.Equal("a", d.RunNext.Single());
    }

    [Fact]
    public async Task Poll_PicksLeastParticipantAmongWilling()
    {
        // b=1,c=1,a=0; lastSpeaker=c → candidatos {a,b} ordenados por eff → [a,b]. Se pollea: a NO quiere,
        // b SÍ → se elige b (el primero que quiere en orden de equidad).
        var poll = new Dictionary<string, IAgent>
        {
            ["a"] = new PollBidder("a", wants: false),
            ["b"] = new PollBidder("b", wants: true),
        };
        var strat = new FairConverseStrategy(["a", "b", "c"], pollAgents: poll, rng: new Random(1));
        var d = await strat.NextAsync(State([Turn("b"), Turn("c")]));
        Assert.Equal("b", d.RunNext.Single());
    }

    [Fact]
    public async Task Poll_NobodyWants_YieldsToHuman()
    {
        // Con poll y NADIE quiere hablar → Stop (la conversación se asentó, cede al humano).
        var poll = new Dictionary<string, IAgent>
        {
            ["a"] = new PollBidder("a", wants: false),
            ["b"] = new PollBidder("b", wants: false),
            ["c"] = new PollBidder("c", wants: false),
        };
        var strat = new FairConverseStrategy(["a", "b", "c"], pollAgents: poll, rng: new Random(1));
        var d = await strat.NextAsync(State([Turn("a")]));
        Assert.True(d.IsTerminal);
    }

    [Fact]
    public async Task OverManyRounds_ParticipationStaysBalanced()
    {
        // Sanity: sin bids, tras N turnos ningún participante queda muy por encima de otro (equidad).
        var strat = new FairConverseStrategy(Roster, maxRounds: 1000, rng: new Random(7));
        var history = new List<NodeResult>();
        for (var i = 0; i < 40; i++)
        {
            var d = await strat.NextAsync(State(history, round: i));
            history.Add(Turn(d.RunNext.Single()));
        }
        var counts = Roster.Select(id => history.Count(h => h.Response.AgentId == id)).ToList();
        Assert.True(counts.Max() - counts.Min() <= 1, $"desbalance: [{string.Join(",", counts)}]");
    }
}
