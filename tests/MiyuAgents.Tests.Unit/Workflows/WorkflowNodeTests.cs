using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MiyuAgents.Core;
using MiyuAgents.Core.Events;
using MiyuAgents.Testing;
using MiyuAgents.Workflows;
using Xunit;

namespace MiyuAgents.Tests.Unit.Workflows;

// ── Fake INodeAgent: produce un NodeResult guionado por nº de invocación, y cuenta/loguea ────
#pragma warning disable CS0067 // eventos del contrato IAgent, no se disparan en tests
file sealed class ScriptedNode(string id, Func<int, NodeResult> script, List<string>? log = null) : INodeAgent
{
    public int Calls { get; private set; }
    public string AgentId => id;
    public string AgentName => id;
    public AgentRole Role => AgentRole.Custom;

    public Task<NodeResult> RunNodeAsync(NodeState state, CancellationToken ct = default)
    {
        Calls++;
        log?.Add(id);
        return Task.FromResult(script(Calls));
    }

    public Task<AgentResponse> ProcessAsync(AgentContext ctx, CancellationToken ct = default)
        => throw new NotSupportedException("el control-loop usa RunNodeAsync");

    public event AsyncEventHandler<MessageReceivedEventArgs>?       OnMessageReceived;
    public event AsyncEventHandler<LlmCallRequestedEventArgs>?      OnLLMCallRequested;
    public event AsyncEventHandler<LlmCallRespondedEventArgs>?      OnLLMCallResponded;
    public event AsyncEventHandler<AgentResponseProducedEventArgs>? OnResponseProduced;
    public event AsyncEventHandler<AgentErrorEventArgs>?            OnError;
}
#pragma warning restore CS0067

file sealed class AlwaysRun(string id) : IControlStrategy
{
    public string Name => "always";
    public Task<ControlDecision> NextAsync(NodeState state, CancellationToken ct = default)
        => Task.FromResult(ControlDecision.Run(id));
}

file sealed class BidWatch(string first) : IControlStrategy
{
    public List<Bid> SeenBids { get; } = [];
    public string Name => "bid-watch";
    public Task<ControlDecision> NextAsync(NodeState state, CancellationToken ct = default)
    {
        SeenBids.Clear();
        SeenBids.AddRange(state.Bids);
        return Task.FromResult(state.History.Count == 0 ? ControlDecision.Run(first) : ControlDecision.Stop());
    }
}

file sealed class RecordingDriver : IDriver
{
    public string? LastAsk { get; private set; }
    public Task<string> AnswerAsync(string ask, NodeState state, CancellationToken ct = default)
    {
        LastAsk = ask;
        return Task.FromResult("oscuro");
    }
}

// ── W2: el runtime (WorkflowNode = control-loop) + strategies triviales + resiliencia ────────
public class WorkflowNodeTests
{
    static readonly ILogger<AgentBase<NodeResult>> Log = NullLogger<AgentBase<NodeResult>>.Instance;

    static NodeResult Res(string id, NodeSignal sig, Artifact? art = null) => new()
    {
        Response = new AgentResponse
        {
            AgentId = id, AgentName = id, Role = AgentRole.Custom,
            Status = sig == NodeSignal.Failed ? AgentStatus.Error : AgentStatus.Ok,
        },
        Signal    = sig,
        Artifacts = art is null ? [] : [art],
        Ask       = sig == NodeSignal.NeedsInput ? "¿tono?" : null,
    };

    static WorkflowNode Node(IControlStrategy strategy, IReadOnlyDictionary<string, IAgent> children,
        ResiliencePolicy? policy = null, IDriver? driver = null)
        => new("root", strategy, children, Log, policy, driver);

    // 1) el loop corre — Sequence respeta el orden y termina Done, juntando artefactos
    [Fact]
    public async Task Sequence_RunsChildrenInOrder_AndCollectsArtifacts()
    {
        var log = new List<string>();
        var a = new ScriptedNode("a", _ => Res("a", NodeSignal.Done, new Artifact("text", "A")), log);
        var b = new ScriptedNode("b", _ => Res("b", NodeSignal.Done, new Artifact("text", "B")), log);
        var c = new ScriptedNode("c", _ => Res("c", NodeSignal.Done, new Artifact("text", "C")), log);
        var node = Node(new SequenceStrategy(["a", "b", "c"]),
            new Dictionary<string, IAgent> { ["a"] = a, ["b"] = b, ["c"] = c });

        var r = await node.RunNodeAsync(new NodeState { Input = "go" });

        r.Signal.Should().Be(NodeSignal.Done);
        log.Should().Equal("a", "b", "c");                        // orden
        (a.Calls, b.Calls, c.Calls).Should().Be((1, 1, 1));
        r.Artifacts.Select(x => x.Name).Should().Equal("A", "B", "C");
        r.Transcript.Where(x => x.Kind == WorkflowTranscriptKind.ChildResult)
            .Select(x => x.NodeId).Should().Equal("a", "b", "c");
    }

    // 2) paralelo — corren todos y termina Done
    [Fact]
    public async Task Parallel_RunsAllChildren_Done()
    {
        var a = new ScriptedNode("a", _ => Res("a", NodeSignal.Done));
        var b = new ScriptedNode("b", _ => Res("b", NodeSignal.Done));
        var c = new ScriptedNode("c", _ => Res("c", NodeSignal.Done));
        var node = Node(new ParallelStrategy(["a", "b", "c"]),
            new Dictionary<string, IAgent> { ["a"] = a, ["b"] = b, ["c"] = c });

        var r = await node.RunNodeAsync(new NodeState { Input = "go" });

        r.Signal.Should().Be(NodeSignal.Done);
        (a.Calls, b.Calls, c.Calls).Should().Be((1, 1, 1));
    }

    // 3) retry — un hijo que falla y después anda se reintenta hasta OK
    [Fact]
    public async Task Retry_RetriesFailedChild_UntilOk()
    {
        var a = new ScriptedNode("a", call => Res("a", call <= 2 ? NodeSignal.Failed : NodeSignal.Done));
        var node = Node(new SequenceStrategy(["a"]),
            new Dictionary<string, IAgent> { ["a"] = a }, new ResiliencePolicy(MaxRetries: 3));

        var r = await node.RunNodeAsync(new NodeState { Input = "go" });

        r.Signal.Should().Be(NodeSignal.Done);
        a.Calls.Should().Be(3);                                  // 2 fallos + 1 ok
    }

    // 4) retry agotado — el fallo SUBE
    [Fact]
    public async Task Retry_Exhausted_BubblesFailed()
    {
        var a = new ScriptedNode("a", _ => Res("a", NodeSignal.Failed));
        var node = Node(new SequenceStrategy(["a"]),
            new Dictionary<string, IAgent> { ["a"] = a }, new ResiliencePolicy(MaxRetries: 1));

        var r = await node.RunNodeAsync(new NodeState { Input = "go" });

        r.Signal.Should().Be(NodeSignal.Failed);
        a.Calls.Should().Be(2);                                  // intento + 1 retry
    }

    // 5) signal sube — NeedsReplanning termina el nodo y lo propaga al padre
    [Fact]
    public async Task Signal_NeedsReplanning_BubblesUp()
    {
        var a = new ScriptedNode("a", _ => Res("a", NodeSignal.NeedsReplanning));
        var node = Node(new SequenceStrategy(["a"]), new Dictionary<string, IAgent> { ["a"] = a });

        var r = await node.RunNodeAsync(new NodeState { Input = "go" });

        r.Signal.Should().Be(NodeSignal.NeedsReplanning);
    }

    // 6) budget corta — una strategy que nunca termina se aborta con Failed
    [Fact]
    public async Task Budget_Cuts_WhenStrategyNeverStops()
    {
        var a = new ScriptedNode("a", _ => Res("a", NodeSignal.Done));
        var node = Node(new AlwaysRun("a"),
            new Dictionary<string, IAgent> { ["a"] = a }, new ResiliencePolicy(MaxSteps: 3));

        var r = await node.RunNodeAsync(new NodeState { Input = "go" });

        r.Signal.Should().Be(NodeSignal.Failed);
        a.Calls.Should().Be(3);                                  // se cortó en MaxSteps
    }

    // 7) NeedsInput — el loop le pregunta al Driver
    [Fact]
    public async Task NeedsInput_AsksTheDriver()
    {
        var a = new ScriptedNode("a", _ => Res("a", NodeSignal.NeedsInput));
        var driver = new RecordingDriver();
        var node = Node(new SequenceStrategy(["a"]), new Dictionary<string, IAgent> { ["a"] = a }, driver: driver);

        var r = await node.RunNodeAsync(new NodeState { Input = "go" });

        driver.LastAsk.Should().Be("¿tono?");
        r.Signal.Should().Be(NodeSignal.Done);
        r.Transcript.Select(x => x.Kind).Should().ContainInOrder(
            WorkflowTranscriptKind.ChildResult,
            WorkflowTranscriptKind.DriverQuestion,
            WorkflowTranscriptKind.DriverAnswer);
    }

    // 8) RequestTurn — encola un bid que la strategy VE al próximo paso
    [Fact]
    public async Task RequestTurn_EnqueuesBid_VisibleToStrategy()
    {
        var a = new ScriptedNode("a", _ => Res("a", NodeSignal.RequestTurn));
        var strat = new BidWatch("a");
        var node = Node(strat, new Dictionary<string, IAgent> { ["a"] = a });

        await node.RunNodeAsync(new NodeState { Input = "go" });

        strat.SeenBids.Should().ContainSingle().Which.NodeId.Should().Be("a");
    }

    // 9) recursión — un WorkflowNode como hijo de otro; el artefacto burbujea
    [Fact]
    public async Task Recursion_NodeInsideNode_BubblesArtifact()
    {
        var leaf  = new ScriptedNode("leaf", _ => Res("leaf", NodeSignal.Done, new Artifact("text", "hola")));
        var inner = Node(new SequenceStrategy(["leaf"]), new Dictionary<string, IAgent> { ["leaf"] = leaf });
        var outer = new WorkflowNode("outer", new SequenceStrategy(["inner"]),
            new Dictionary<string, IAgent> { ["inner"] = inner }, Log);

        var r = await outer.RunNodeAsync(new NodeState { Input = "go" });

        r.Signal.Should().Be(NodeSignal.Done);
        r.Artifacts.Should().ContainSingle().Which.Name.Should().Be("hola");
        r.Transcript.Select(x => x.NodeId).Should().ContainInOrder("leaf", "inner");
        leaf.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Transcript_IsBoundedAndMarksTruncation()
    {
        var a = new ScriptedNode("a", _ => Res("a", NodeSignal.Done));
        var node = Node(
            new AlwaysRun("a"),
            new Dictionary<string, IAgent> { ["a"] = a },
            new ResiliencePolicy(MaxSteps: 8, MaxTranscriptEntries: 3));

        var r = await node.RunNodeAsync(new NodeState { Input = "go" });

        r.Transcript.Should().HaveCount(3);
        r.Transcript[0].Kind.Should().Be(WorkflowTranscriptKind.Truncated);
    }

    [Fact]
    public async Task Transcript_BoundsTextAndDoesNotRetainHeavyArtifactPayloads()
    {
        var longText = new string('x', 500);
        var payload = new byte[1_000_000];
        var a = new ScriptedNode("a", _ => new NodeResult
        {
            Response = new AgentResponse
            {
                AgentId = "a", AgentName = "a", Role = AgentRole.Custom, Data = longText,
            },
            Artifacts = [new Artifact("image", "large", payload)],
        });
        var node = Node(
            new SequenceStrategy(["a"]),
            new Dictionary<string, IAgent> { ["a"] = a },
            new ResiliencePolicy(MaxTranscriptTextLength: 64));

        var r = await node.RunNodeAsync(new NodeState { Input = "go" });
        var entry = r.Transcript.Single();

        entry.Text.Should().HaveLength(64).And.EndWith("…");
        entry.Artifacts.Single().Preview.Should().Be("<Byte[]>");
    }

    // 10) un IAgent COMÚN (sin signals) se envuelve a Done
    [Fact]
    public async Task PlainAgent_IsWrapped_ToDone()
    {
        var bot  = ScriptedAgent.Constant("bot", "Bot", "hola");
        var node = Node(new SequenceStrategy(["bot"]), new Dictionary<string, IAgent> { ["bot"] = bot });

        var r = await node.RunNodeAsync(new NodeState { Input = "go" });

        r.Signal.Should().Be(NodeSignal.Done);
    }
}
