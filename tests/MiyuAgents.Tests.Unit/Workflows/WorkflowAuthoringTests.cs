using FluentAssertions;
using MiyuAgents.Core;
using MiyuAgents.Core.Events;
using MiyuAgents.Testing;
using MiyuAgents.Workflows;
using Xunit;

namespace MiyuAgents.Tests.Unit.Workflows;

#pragma warning disable CS0067
// Hoja que produce Done + un artefacto (para ver la recursión burbujear).
file sealed class Leaf(string id, string artifact, List<string>? log = null) : INodeAgent
{
    public string AgentId => id;
    public string AgentName => id;
    public AgentRole Role => AgentRole.Custom;
    public Task<NodeResult> RunNodeAsync(NodeState state, CancellationToken ct = default)
    {
        log?.Add(id);
        return Task.FromResult(new NodeResult
        {
            Response  = new AgentResponse { AgentId = id, AgentName = id, Role = AgentRole.Custom },
            Signal    = NodeSignal.Done,
            Artifacts = [new Artifact("text", artifact)],
        });
    }
    public Task<AgentResponse> ProcessAsync(AgentContext ctx, CancellationToken ct = default)
        => throw new NotSupportedException();
    public event AsyncEventHandler<MessageReceivedEventArgs>?       OnMessageReceived;
    public event AsyncEventHandler<LlmCallRequestedEventArgs>?      OnLLMCallRequested;
    public event AsyncEventHandler<LlmCallRespondedEventArgs>?      OnLLMCallResponded;
    public event AsyncEventHandler<AgentResponseProducedEventArgs>? OnResponseProduced;
    public event AsyncEventHandler<AgentErrorEventArgs>?            OnError;
}
#pragma warning restore CS0067

// ── W5: authoring en runtime (Spec → Builder → Registry → Store, hot-refresh) ────────────────
public class WorkflowAuthoringTests
{
    static WorkflowRegistry Reg(params (string Id, IAgent Agent)[] agents)
        => new(agents.ToDictionary(a => a.Id, a => a.Agent));

    // ── Un WorkflowSpec (data) se instancia y corre ─────────────────────────────
    [Fact]
    public async Task Build_SimpleSequenceSpec_Runs()
    {
        var log = new List<string>();
        var reg = Reg(("a", new Leaf("a", "A", log)), ("b", new Leaf("b", "B", log)));
        var spec = new WorkflowSpec("wf", "Test", "desc",
            new NodeSpec("root", "sequence", ["a", "b"]));

        var node = WorkflowBuilder.Build(spec, reg);
        var r    = await node.RunNodeAsync(new NodeState { Input = "go" });

        r.Signal.Should().Be(NodeSignal.Done);
        log.Should().Equal("a", "b");
        r.Artifacts.Select(x => x.Name).Should().Equal("A", "B");
    }

    // ── Recursión: un miembro es un sub-NodeSpec (sub-workflow) ─────────────────
    [Fact]
    public async Task Build_RecursiveSpec_RunsSubWorkflow_AndBubblesArtifact()
    {
        var reg = Reg(("leaf", new Leaf("leaf", "hola")));
        var spec = new WorkflowSpec("wf", "Test", "desc",
            new NodeSpec("root", "sequence", ["inner"],
                Children: [new NodeSpec("inner", "sequence", ["leaf"])]));

        var node = WorkflowBuilder.Build(spec, reg);
        var r    = await node.RunNodeAsync(new NodeState { Input = "go" });

        r.Signal.Should().Be(NodeSignal.Done);
        r.Artifacts.Should().ContainSingle().Which.Name.Should().Be("hola");
    }

    // ── HOT-REFRESH: editar el spec en el store cambia el comportamiento SIN rebuild ──
    [Fact]
    public async Task HotRefresh_EditingSpec_ChangesBehaviour_NoRestart()
    {
        var log = new List<string>();
        var reg = Reg(("a", new Leaf("a", "A", log)), ("b", new Leaf("b", "B", log)));
        var store = new InMemoryWorkflowStore();

        // v1: sólo corre "a".
        store.Save(new WorkflowSpec("wf", "Test", "v1", new NodeSpec("root", "sequence", ["a"])));
        await WorkflowBuilder.Build(store.Get("wf")!, reg).RunNodeAsync(new NodeState { Input = "go" });
        log.Should().Equal("a");

        // v2: mismo id, ahora corre "a" y "b" — editado en caliente, sin reiniciar nada.
        log.Clear();
        store.Save(new WorkflowSpec("wf", "Test", "v2", new NodeSpec("root", "sequence", ["a", "b"])));
        await WorkflowBuilder.Build(store.Get("wf")!, reg).RunNodeAsync(new NodeState { Input = "go" });
        log.Should().Equal("a", "b");
    }

    // ── Extensibilidad: una strategy CUSTOM registrada por nombre ───────────────
    [Fact]
    public async Task CustomStrategy_CanBeRegistered_ByName()
    {
        var extra = new Dictionary<string, Func<NodeSpec, IControlStrategy>>
        {
            ["only-first"] = s => new SequenceStrategy([s.Members[0]]),   // corre sólo el primer miembro
        };
        var log = new List<string>();
        var reg = new WorkflowRegistry(
            new Dictionary<string, IAgent> { ["a"] = new Leaf("a", "A", log), ["b"] = new Leaf("b", "B", log) },
            extra);
        var spec = new WorkflowSpec("wf", "Test", "desc", new NodeSpec("root", "only-first", ["a", "b"]));

        await WorkflowBuilder.Build(spec, reg).RunNodeAsync(new NodeState { Input = "go" });

        log.Should().Equal("a");                                        // usó la strategy custom
    }

    // ── Errores claros: agente/strategy desconocidos ────────────────────────────
    [Fact]
    public void Build_UnknownAgent_Throws()
    {
        var reg = Reg();
        var spec = new WorkflowSpec("wf", "Test", "desc", new NodeSpec("root", "sequence", ["nope"]));
        var act = () => WorkflowBuilder.Build(spec, reg);
        act.Should().Throw<InvalidOperationException>().WithMessage("*nope*");
    }

    [Fact]
    public void Build_UnknownStrategy_Throws()
    {
        var reg = Reg(("a", new Leaf("a", "A")));
        var spec = new WorkflowSpec("wf", "Test", "desc", new NodeSpec("root", "no-such-strategy", ["a"]));
        var act = () => WorkflowBuilder.Build(spec, reg);
        act.Should().Throw<InvalidOperationException>().WithMessage("*no-such-strategy*");
    }

    [Fact]
    public void Build_SelfReferentialSpec_FailsClearlyInsteadOfOverflowing()
    {
        var children = new List<NodeSpec>();
        var root = new NodeSpec("root", "sequence", ["root"], children);
        children.Add(root);
        var spec = new WorkflowSpec("wf", "Recursive", "invalid structural cycle", root);

        var act = () => WorkflowBuilder.Build(spec, Reg());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*cycle*RecursiveWorkflowNode*");
    }

    // ── El Store guarda/lista/borra ─────────────────────────────────────────────
    [Fact]
    public void Store_SavesListsAndRemoves()
    {
        var store = new InMemoryWorkflowStore();
        store.Save(new WorkflowSpec("a", "A", "", new NodeSpec("r", "sequence", [])));
        store.Save(new WorkflowSpec("b", "B", "", new NodeSpec("r", "sequence", [])));

        store.List().Should().HaveCount(2);
        store.Get("a")!.DisplayName.Should().Be("A");
        store.Remove("a").Should().BeTrue();
        store.Get("a").Should().BeNull();
        store.List().Should().ContainSingle();
    }
}
