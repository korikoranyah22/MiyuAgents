using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MiyuAgents.Core;
using MiyuAgents.Core.Events;
using MiyuAgents.Workflows;
using Xunit;

namespace MiyuAgents.Tests.Unit.Workflows;

// ── Fake thread-safe: la hoja cuenta llamadas con Interlocked y produce su artefacto ─────────
#pragma warning disable CS0067
file sealed class ConcLeaf(string id) : INodeAgent
{
    int _calls;

    public int Calls => Volatile.Read(ref _calls);
    public string AgentId => id;
    public string AgentName => id;
    public AgentRole Role => AgentRole.Custom;

    public async Task<NodeResult> RunNodeAsync(NodeState state, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _calls);
        await Task.Delay(1, ct);                                   // fuerza interleaving real entre runs
        return new NodeResult
        {
            Response  = new AgentResponse { AgentId = id, AgentName = id, Role = AgentRole.Custom },
            Signal    = NodeSignal.Done,
            Artifacts = [new Artifact("text", id)],
        };
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

// ── Evidencia de concurrencia (para API-REAL.md): el MISMO WorkflowNode corre en paralelo ────
// El nodo es stateless por-run (el NodeState se pasa y es inmutable; el loop es local; los
// scopes AsyncLocal quedan aislados por rama) → reentrante. Lo que se comparte (hijos, strategy,
// driver, sink) debe ser thread-safe; acá lo son.
public class WorkflowConcurrencyTests
{
    static readonly ILogger<AgentBase<NodeResult>> Log = NullLogger<AgentBase<NodeResult>>.Instance;

    static WorkflowNode Node(IControlStrategy s, IReadOnlyDictionary<string, IAgent> children)
        => new("root", s, children, Log);

    static async Task<NodeResult[]> RunConcurrent(WorkflowNode node, int runs)
        => await Task.WhenAll(Enumerable.Range(0, runs)
            .Select(_ => node.RunNodeAsync(new NodeState { Input = "go" })));

    // ── 1) mismo nodo, N runs en paralelo: resultados completos y aislados ────
    [Fact]
    public async Task SameNode_ManyConcurrentRuns_AreIsolatedAndComplete()
    {
        var a = new ConcLeaf("a");
        var b = new ConcLeaf("b");
        var c = new ConcLeaf("c");
        var node = Node(new SequenceStrategy(["a", "b", "c"]),
            new Dictionary<string, IAgent> { ["a"] = a, ["b"] = b, ["c"] = c });

        const int runs = 20;
        var results = await RunConcurrent(node, runs);

        results.Should().HaveCount(runs);
        results.Should().OnlyContain(r => r.Signal == NodeSignal.Done);
        foreach (var r in results)
            r.Artifacts.Select(x => x.Name).Should().Equal("a", "b", "c");   // cada run ve SU árbol completo
        (a.Calls, b.Calls, c.Calls).Should().Be((runs, runs, runs));         // sin perder llamadas
    }

    // ── 2) nodo RECURSIVO (outer → inner → leaf) en paralelo: artefacto por run ──
    [Fact]
    public async Task RecursiveNode_ConcurrentRuns_BubbleArtifactsPerRun()
    {
        var leaf  = new ConcLeaf("leaf");
        var inner = Node(new SequenceStrategy(["leaf"]), new Dictionary<string, IAgent> { ["leaf"] = leaf });
        var outer = new WorkflowNode("outer", new SequenceStrategy(["inner"]),
            new Dictionary<string, IAgent> { ["inner"] = inner }, Log);

        const int runs = 10;
        var results = await RunConcurrent(outer, runs);

        results.Should().HaveCount(runs);
        results.Should().OnlyContain(r => r.Signal == NodeSignal.Done);
        foreach (var r in results)
            r.Artifacts.Should().ContainSingle().Which.Name.Should().Be("leaf");
        leaf.Calls.Should().Be(runs);
    }

    // ── 3) con TRACE encendido: lanes correctos y sink consistente bajo runs concurrentes ──
    [Fact]
    public async Task ConcurrentRuns_WithTraceSink_KeepLanesConsistent()
    {
        var a = new ConcLeaf("a");
        var b = new ConcLeaf("b");
        var c = new ConcLeaf("c");
        var node = Node(new SequenceStrategy(["a", "b", "c"]),
            new Dictionary<string, IAgent> { ["a"] = a, ["b"] = b, ["c"] = c });
        var sink = new InMemoryTraceSink();

        const int runs = 10;
        using (NodeTrace.Begin(sink))
            await RunConcurrent(node, runs);

        var events = sink.Events;
        events.Count(e => e is { Kind: TraceKind.NodeStart, Lane: "root" }).Should().Be(runs);
        events.Count(e => e is { Kind: TraceKind.ChildResult, Lane: "root/a" }).Should().Be(runs);
        events.Count(e => e is { Kind: TraceKind.ChildResult, Lane: "root/b" }).Should().Be(runs);
        events.Count(e => e is { Kind: TraceKind.ChildResult, Lane: "root/c" }).Should().Be(runs);
        events.Count(e => e is { Kind: TraceKind.NodeEnd, Lane: "root" }).Should().Be(runs);
        events.Should().HaveCount(runs * 5);   // start + 3 child-results + end por run, sin pérdidas
    }
}
