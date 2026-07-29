using FluentAssertions;
using MiyuAgents.Core;
using MiyuAgents.Workflows;
using Xunit;

namespace MiyuAgents.Tests.Unit.Workflows;

// ── Fakes de FORMA: prueban que las interfaces del contrato se pueden implementar ────────────
file sealed class FakeSequence(params string[] ids) : IControlStrategy
{
    public string Name => "fake-sequence";
    // Corre los ids una vez; si ya hay historial → termina (Done).
    public Task<ControlDecision> NextAsync(NodeState state, CancellationToken ct = default)
        => Task.FromResult(state.History.Count == 0 ? ControlDecision.Run(ids) : ControlDecision.Stop());
}

file sealed class EchoDriver : IDriver
{
    public Task<string> AnswerAsync(string ask, NodeState state, CancellationToken ct = default)
        => Task.FromResult($"respuesta a: {ask}");
}

// ── W1: contratos núcleo del framework (MiyuAgents.Workflows) — shape tests ──────────────────
public class WorkflowContractTests
{
    static AgentResponse Resp(string id = "n1", object? data = null) =>
        new() { AgentId = id, AgentName = id.ToUpperInvariant(), Role = AgentRole.Orchestration, Data = data ?? "ok" };

    // ── NodeSignal ───────────────────────────────────────────────────────────
    [Fact]
    public void NodeSignal_HasTheExpectedVocabulary()
    {
        Enum.GetValues<NodeSignal>().Should().BeEquivalentTo(new[]
        {
            NodeSignal.Done, NodeSignal.NeedsInput, NodeSignal.NeedsReplanning,
            NodeSignal.Failed, NodeSignal.HandBack, NodeSignal.Continue, NodeSignal.RequestTurn,
        });
    }

    // ── NodeResult ───────────────────────────────────────────────────────────
    [Fact]
    public void NodeResult_From_DefaultsToDone_WithNoArtifacts()
    {
        var r = NodeResult.From(Resp());
        r.Signal.Should().Be(NodeSignal.Done);
        r.Artifacts.Should().BeEmpty();
        r.Ask.Should().BeNull();
        r.Response.Data.Should().Be("ok");
    }

    [Fact]
    public void NodeResult_CarriesSignalArtifactsAndAsk()
    {
        var art = new Artifact("text", "cap-1", "había una vez");
        var r = NodeResult.From(Resp(), NodeSignal.NeedsInput, [art], ask: "¿qué tono?");
        r.Signal.Should().Be(NodeSignal.NeedsInput);
        r.Artifacts.Should().ContainSingle().Which.Should().Be(art);
        r.Ask.Should().Be("¿qué tono?");
    }

    // ── Artifact ─────────────────────────────────────────────────────────────
    [Fact]
    public void Artifact_IsDomainNeutral_KindRequired_RestOptional()
    {
        var a = new Artifact("file", Name: "main.py", Payload: "print(1)", Id: "a1");
        a.Kind.Should().Be("file");
        a.Name.Should().Be("main.py");
        (a.Payload as string).Should().Be("print(1)");

        var bare = new Artifact("plan");
        bare.Name.Should().BeNull();
        bare.Payload.Should().BeNull();
    }

    // ── Bid ──────────────────────────────────────────────────────────────────
    [Fact]
    public void Bid_DefaultsPriorityZeroAndNoReason()
    {
        var b = new Bid("kori");
        b.Priority.Should().Be(0);
        b.Reason.Should().BeNull();
    }

    // ── ControlDecision ──────────────────────────────────────────────────────
    [Fact]
    public void ControlDecision_Stop_IsTerminal_WithEmitSignal()
    {
        var d = ControlDecision.Stop(NodeSignal.NeedsReplanning);
        d.IsTerminal.Should().BeTrue();
        d.RunNext.Should().BeEmpty();
        d.Emit.Should().Be(NodeSignal.NeedsReplanning);
    }

    [Fact]
    public void ControlDecision_Run_Sequential_And_RunParallel()
    {
        var seq = ControlDecision.Run("a", "b");
        seq.IsTerminal.Should().BeFalse();
        seq.Parallel.Should().BeFalse();
        seq.RunNext.Should().Equal("a", "b");

        var par = ControlDecision.RunParallel("a", "b", "c");
        par.Parallel.Should().BeTrue();
        par.RunNext.Should().HaveCount(3);
    }

    // ── NodeState ────────────────────────────────────────────────────────────
    [Fact]
    public void NodeState_DefaultsAreEmpty()
    {
        var s = new NodeState { Input = "hacé X" };
        s.Round.Should().Be(0);
        s.History.Should().BeEmpty();
        s.Bids.Should().BeEmpty();
    }

    [Fact]
    public void NodeState_With_ProducesImmutableCopy()
    {
        var s0 = new NodeState { Input = "x" };
        var s1 = s0 with { Round = 1, Bids = [new Bid("kori", 5, "quiero opinar")] };

        s0.Round.Should().Be(0);          // el original NO muta
        s0.Bids.Should().BeEmpty();
        s1.Round.Should().Be(1);
        s1.Bids.Should().ContainSingle().Which.NodeId.Should().Be("kori");
    }

    // ── IControlStrategy / IDriver: forma (se pueden implementar y usar) ──────
    [Fact]
    public async Task IControlStrategy_CanBeImplemented_AndDecides()
    {
        IControlStrategy s = new FakeSequence("a", "b");

        var first = await s.NextAsync(new NodeState { Input = "x" });
        first.RunNext.Should().Equal("a", "b");

        var afterRun = await s.NextAsync(new NodeState { Input = "x", History = [NodeResult.From(Resp())] });
        afterRun.IsTerminal.Should().BeTrue();   // ya corrió → termina
    }

    [Fact]
    public async Task IDriver_CanBeImplemented_AndAnswers()
    {
        IDriver human = new EchoDriver();
        var answer = await human.AnswerAsync("¿seguimos?", new NodeState { Input = "x" });
        answer.Should().Be("respuesta a: ¿seguimos?");
    }
}
