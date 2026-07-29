using FluentAssertions;
using MiyuAgents.Workflows;
using Xunit;

namespace MiyuAgents.Tests.Unit.Workflows;

public class SystemDriverTests
{
    [Fact]
    public async Task AnswersSeeds_InOrder_ThenFallsBack_NeverBlocks()
    {
        var driver = new SystemDriver("una selfie en la hamaca", "atardecer");
        var s = new NodeState { Input = "x" };

        (await driver.AnswerAsync("¿escena?", s)).Should().Be("una selfie en la hamaca");
        (await driver.AnswerAsync("¿luz?", s)).Should().Be("atardecer");
        (await driver.AnswerAsync("¿algo más?", s)).Should().Contain("criterio");   // agotadas → criterio propio
        driver.Answered.Should().Be(3);
    }

    [Fact]
    public async Task CustomFallback()
    {
        var driver = new SystemDriver(answers: null, fallback: "usa defaults");
        (await driver.AnswerAsync("?", new NodeState { Input = "x" })).Should().Be("usa defaults");
    }
}

// ── NodePlugins (el entorno ambiente de la corrida) ──────────────────────────────────────────
file sealed class FakePlugin(string kind) : IWorkflowPlugin { public string Kind => kind; }

public class NodePluginsTests
{
    [Fact]
    public void Get_ReturnsTypedPlugin_InsideScope_AndNullOutside()
    {
        NodePlugins.Get<FakePlugin>().Should().BeNull();          // fuera de un scope: nada

        using (NodePlugins.Begin(new FakePlugin("photo-run")))
        {
            NodePlugins.Get<FakePlugin>()!.Kind.Should().Be("photo-run");
            NodePlugins.Current.Should().HaveCount(1);
        }

        NodePlugins.Get<FakePlugin>().Should().BeNull();          // restaurado al salir
    }

    [Fact]
    public async Task FlowsAcrossAsync_AndIsolatedPerBranch()
    {
        // AsyncLocal: fluye a través de awaits y queda aislado por rama (fan-out paralelo).
        async Task<string?> Branch(string kind)
        {
            using (NodePlugins.Begin(new FakePlugin(kind)))
            {
                await Task.Delay(10);
                return NodePlugins.Get<FakePlugin>()?.Kind;
            }
        }

        var results = await Task.WhenAll(Branch("a"), Branch("b"));
        results.Should().BeEquivalentTo(["a", "b"]);
    }
}
