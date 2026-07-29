using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MiyuAgents.Core;
using MiyuAgents.Core.Events;
using MiyuAgents.Workflows;
using Xunit;

namespace MiyuAgents.Tests.Unit.Workflows;

// ── Fakes locales (file-scoped) ──────────────────────────────────────────────────────────────
#pragma warning disable CS0067
file sealed class Node(string id, Func<int, NodeResult> script, List<string>? log = null) : INodeAgent
{
    public int Calls { get; private set; }
    public string AgentId => id;
    public string AgentName => id;
    public AgentRole Role => AgentRole.Custom;
    public Task<NodeResult> RunNodeAsync(NodeState state, CancellationToken ct = default)
    { Calls++; log?.Add(id); return Task.FromResult(script(Calls)); }
    public Task<AgentResponse> ProcessAsync(AgentContext ctx, CancellationToken ct = default)
        => throw new NotSupportedException();
    public event AsyncEventHandler<MessageReceivedEventArgs>?       OnMessageReceived;
    public event AsyncEventHandler<LlmCallRequestedEventArgs>?      OnLLMCallRequested;
    public event AsyncEventHandler<LlmCallRespondedEventArgs>?      OnLLMCallResponded;
    public event AsyncEventHandler<AgentResponseProducedEventArgs>? OnResponseProduced;
    public event AsyncEventHandler<AgentErrorEventArgs>?            OnError;
}

// Un participante que SIEMPRE quiere hablar (poll proactivo del bidding).
file sealed class EagerBidder(string id) : INodeAgent, IBiddingParticipant
{
    public string AgentId => id;
    public string AgentName => id;
    public AgentRole Role => AgentRole.Custom;
    public Task<bool> WantsTurnAsync(NodeState state, CancellationToken ct = default) => Task.FromResult(true);
    public Task<NodeResult> RunNodeAsync(NodeState state, CancellationToken ct = default)
        => Task.FromResult(new NodeResult { Response = new AgentResponse { AgentId = id, AgentName = id, Role = AgentRole.Custom }, Signal = NodeSignal.Done });
    public Task<AgentResponse> ProcessAsync(AgentContext ctx, CancellationToken ct = default)
        => throw new NotSupportedException();
    public event AsyncEventHandler<MessageReceivedEventArgs>?       OnMessageReceived;
    public event AsyncEventHandler<LlmCallRequestedEventArgs>?      OnLLMCallRequested;
    public event AsyncEventHandler<LlmCallRespondedEventArgs>?      OnLLMCallResponded;
    public event AsyncEventHandler<AgentResponseProducedEventArgs>? OnResponseProduced;
    public event AsyncEventHandler<AgentErrorEventArgs>?            OnError;
}
#pragma warning restore CS0067

// ── W3: las strategies reales (Loop / Deliberate / plan-execute loop-back) + bidding ─────────
public class WorkflowStrategiesTests
{
    static readonly ILogger<AgentBase<NodeResult>> Log = NullLogger<AgentBase<NodeResult>>.Instance;

    static NodeResult R(string id, NodeSignal sig, Artifact? art = null) => new()
    {
        Response = new AgentResponse
        {
            AgentId = id, AgentName = id, Role = AgentRole.Custom,
            Status = sig == NodeSignal.Failed ? AgentStatus.Error : AgentStatus.Ok,
        },
        Signal = sig,
        Artifacts = art is null ? [] : [art],
    };

    static WorkflowNode Wf(IControlStrategy s, IReadOnlyDictionary<string, IAgent> children, ResiliencePolicy? p = null)
        => new("root", s, children, Log, p);

    // ── LoopStrategy (ReAct): corre un agente hasta que emite Done ───────────────
    [Fact]
    public async Task Loop_RunsAgentUntilDone()
    {
        var koda = new Node("koda", call => R("koda", call < 3 ? NodeSignal.Continue : NodeSignal.Done));
        var node = Wf(new LoopStrategy("koda"), new Dictionary<string, IAgent> { ["koda"] = koda });

        var r = await node.RunNodeAsync(new NodeState { Input = "codeá X" });

        r.Signal.Should().Be(NodeSignal.Done);
        koda.Calls.Should().Be(3);                                    // Continue, Continue, Done
    }

    // ── DeliberateStrategy: fases en orden (comprensión paralela → síntesis → review) ──
    [Fact]
    public async Task Deliberate_RunsPhasesInOrder()
    {
        var log = new List<string>();
        var a = new Node("a", _ => R("a", NodeSignal.Done), log);
        var b = new Node("b", _ => R("b", NodeSignal.Done), log);
        var s = new Node("s", _ => R("s", NodeSignal.Done), log);
        var rev = new Node("r", _ => R("r", NodeSignal.Done), log);
        var node = Wf(new DeliberateStrategy([
            new Phase(["a", "b"], Parallel: true, Name: "comprensión"),
            new Phase(["s"], Name: "síntesis"),
            new Phase(["r"], Name: "review"),
        ]), new Dictionary<string, IAgent> { ["a"] = a, ["b"] = b, ["s"] = s, ["r"] = rev });

        var r = await node.RunNodeAsync(new NodeState { Input = "teoría" });

        r.Signal.Should().Be(NodeSignal.Done);
        log.Take(2).Should().BeEquivalentTo(new[] { "a", "b" });      // fase 1 (paralela, orden libre)
        log.Skip(2).Should().Equal("s", "r");                        // luego síntesis, luego review
    }

    // ── LOOP-BACK: el ejecutor pide replan → vuelve al planning (ISignalReactiveStrategy) ──
    [Fact]
    public async Task PlanExecute_LoopsBackToPlanning_OnNeedsReplanning()
    {
        var plan = new Node("plan", _ => R("plan", NodeSignal.Done));
        var exec = new Node("exec", call => R("exec", call == 1 ? NodeSignal.NeedsReplanning : NodeSignal.Done));
        var node = Wf(new PlanExecuteStrategy("plan", "exec", maxReplans: 2),
            new Dictionary<string, IAgent> { ["plan"] = plan, ["exec"] = exec });

        var r = await node.RunNodeAsync(new NodeState { Input = "construí X" });

        r.Signal.Should().Be(NodeSignal.Done);
        plan.Calls.Should().Be(2);                                    // planeó, re-planeó
        exec.Calls.Should().Be(2);                                    // falló, después anduvo
    }

    // ── LOOP-BACK acotado: si el ejecutor nunca puede, el signal termina subiendo ──
    [Fact]
    public async Task PlanExecute_Bounded_BubblesReplanning_WhenExhausted()
    {
        var plan = new Node("plan", _ => R("plan", NodeSignal.Done));
        var exec = new Node("exec", _ => R("exec", NodeSignal.NeedsReplanning));
        var node = Wf(new PlanExecuteStrategy("plan", "exec", maxReplans: 1),
            new Dictionary<string, IAgent> { ["plan"] = plan, ["exec"] = exec });

        var r = await node.RunNodeAsync(new NodeState { Input = "imposible" });

        r.Signal.Should().Be(NodeSignal.NeedsReplanning);            // agotó los replans → sube
    }

    // ── ConverseStrategy (bidding, unit): el bid gana, pero NO dos veces seguidas ──
    [Fact]
    public async Task Converse_HonorsBid_ButNotBackToBack()
    {
        var s = new ConverseStrategy(["a", "b", "c"], maxRounds: 10);

        // Con un bid de "a" y el último en hablar fue "b" → habla "a" (por el bid).
        var d1 = await s.NextAsync(new NodeState
        {
            Input = "x", Round = 1, Bids = [new Bid("a")],
            History = [R("b", NodeSignal.Done)],
        });
        d1.RunNext.Should().Equal("a");

        // Con un bid de "a" pero "a" ACABA de hablar → NO se honra (anti-monopolio) → round-robin.
        var d2 = await s.NextAsync(new NodeState
        {
            Input = "x", Round = 1, Bids = [new Bid("a")],
            History = [R("a", NodeSignal.Done)],
        });
        d2.RunNext.Should().Equal("b");                              // roster[1 % 3]

        // Sin bids → round-robin puro.
        var d3 = await s.NextAsync(new NodeState { Input = "x", Round = 0 });
        d3.RunNext.Should().Equal("a");                             // roster[0]
    }

    // ── ConverseStrategy: poll proactivo (IBiddingParticipant "quiere hablar") ──
    [Fact]
    public async Task Converse_ProactivePoll_SelectsWillingParticipant()
    {
        var eager = new EagerBidder("c");
        var agents = new Dictionary<string, IAgent> { ["a"] = new Node("a", _ => R("a", NodeSignal.Done)), ["c"] = eager };
        var s = new ConverseStrategy(["a", "c"], maxRounds: 10, pollAgents: agents);

        // Round 1: round-robin daría roster[1]="c" igual... usamos Round 0 para que RR dé "a",
        // pero el poll de "c" (quiere hablar) gana antes del round-robin.
        var d = await s.NextAsync(new NodeState { Input = "x", Round = 0 });

        d.RunNext.Should().Equal("c");                              // el poll seleccionó al que quiere hablar
    }

    // ── ConverseStrategy (integración): termina en maxRounds, no loopea infinito ──
    [Fact]
    public async Task Converse_TerminatesAtMaxRounds()
    {
        var log = new List<string>();
        var a = new Node("a", _ => R("a", NodeSignal.Done), log);
        var b = new Node("b", _ => R("b", NodeSignal.Done), log);
        var node = Wf(new ConverseStrategy(["a", "b"], maxRounds: 4),
            new Dictionary<string, IAgent> { ["a"] = a, ["b"] = b });

        var r = await node.RunNodeAsync(new NodeState { Input = "charla" });

        r.Signal.Should().Be(NodeSignal.Done);
        log.Should().Equal("a", "b", "a", "b");                     // round-robin × 4 rondas
    }
}
