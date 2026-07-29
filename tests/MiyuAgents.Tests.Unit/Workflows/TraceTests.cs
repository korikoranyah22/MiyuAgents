using FluentAssertions;
using MiyuAgents.Core;
using MiyuAgents.Core.Events;
using MiyuAgents.Workflows;
using Xunit;

namespace MiyuAgents.Tests.Unit.Workflows;

#pragma warning disable CS0067
file sealed class TLeaf(string id) : INodeAgent
{
    public string AgentId => id;
    public string AgentName => id;
    public AgentRole Role => AgentRole.Custom;
    public Task<NodeResult> RunNodeAsync(NodeState state, CancellationToken ct = default)
        => Task.FromResult(new NodeResult { Response = new AgentResponse { AgentId = id, AgentName = id, Role = AgentRole.Custom }, Signal = NodeSignal.Done });
    public Task<AgentResponse> ProcessAsync(AgentContext ctx, CancellationToken ct = default) => throw new NotSupportedException();
    public event AsyncEventHandler<MessageReceivedEventArgs>?       OnMessageReceived;
    public event AsyncEventHandler<LlmCallRequestedEventArgs>?      OnLLMCallRequested;
    public event AsyncEventHandler<LlmCallRespondedEventArgs>?      OnLLMCallResponded;
    public event AsyncEventHandler<AgentResponseProducedEventArgs>? OnResponseProduced;
    public event AsyncEventHandler<AgentErrorEventArgs>?            OnError;
}
#pragma warning restore CS0067

// ── W7: trace/observabilidad (INodeTraceSink + NodeScope lane-path, recursivo) ───────────────
public class TraceTests
{
    static WorkflowNode Build(WorkflowSpec spec)
        => WorkflowBuilder.Build(spec, new WorkflowRegistry(new Dictionary<string, IAgent>
        {
            ["a"] = new TLeaf("a"), ["b"] = new TLeaf("b"), ["leaf"] = new TLeaf("leaf"),
        }));

    [Fact]
    public async Task SingleNode_EmitsStart_ChildResults_End()
    {
        var node = Build(new WorkflowSpec("wf", "T", "", new NodeSpec("outer", "sequence", ["a", "b"])));
        var sink = new InMemoryTraceSink();

        using (NodeTrace.Begin(sink))
            await node.RunNodeAsync(new NodeState { Input = "go" });

        var e = sink.Events;
        e.Should().Contain(x => x.Kind == TraceKind.NodeStart && x.Lane == "outer");
        e.Should().Contain(x => x.Kind == TraceKind.ChildResult && x.Lane == "outer/a" && x.Actor == "a");
        e.Should().Contain(x => x.Kind == TraceKind.ChildResult && x.Lane == "outer/b" && x.Actor == "b");
        e[^1].Should().Match<TraceEvent>(x => x.Kind == TraceKind.NodeEnd && x.Lane == "outer");
    }

    [Fact]
    public async Task NestedWorkflow_EmitsHierarchicalLanePaths()
    {
        // outer → inner → leaf
        var node = Build(new WorkflowSpec("wf", "T", "",
            new NodeSpec("outer", "sequence", ["inner"],
                Children: [new NodeSpec("inner", "sequence", ["leaf"])])));
        var sink = new InMemoryTraceSink();

        using (NodeTrace.Begin(sink))
            await node.RunNodeAsync(new NodeState { Input = "go" });

        var lanes = sink.Events.Select(x => (x.Kind, x.Lane)).ToList();
        lanes.Should().Contain((TraceKind.NodeStart, "outer"));
        lanes.Should().Contain((TraceKind.NodeStart, "outer/inner"));         // el sub-workflow bajo su path
        lanes.Should().Contain((TraceKind.ChildResult, "outer/inner/leaf"));  // la hoja, profundidad 3
        lanes.Should().Contain((TraceKind.NodeEnd, "outer/inner"));
        lanes.Should().Contain((TraceKind.NodeEnd, "outer"));

        // Anidamiento: el inner arranca DESPUÉS del outer y termina ANTES.
        var idxOuterStart = sink.Events.ToList().FindIndex(x => x is { Kind: TraceKind.NodeStart, Lane: "outer" });
        var idxInnerEnd   = sink.Events.ToList().FindIndex(x => x is { Kind: TraceKind.NodeEnd,   Lane: "outer/inner" });
        var idxOuterEnd   = sink.Events.ToList().FindIndex(x => x is { Kind: TraceKind.NodeEnd,   Lane: "outer" });
        idxOuterStart.Should().BeLessThan(idxInnerEnd);
        idxInnerEnd.Should().BeLessThan(idxOuterEnd);
    }

    [Fact]
    public async Task NoSink_NoEmission_ZeroOverhead()
    {
        var node = Build(new WorkflowSpec("wf", "T", "", new NodeSpec("outer", "sequence", ["a"])));
        // sin NodeTrace.Begin → no debe romper ni emitir nada
        var r = await node.RunNodeAsync(new NodeState { Input = "go" });
        r.Signal.Should().Be(NodeSignal.Done);
    }
}
