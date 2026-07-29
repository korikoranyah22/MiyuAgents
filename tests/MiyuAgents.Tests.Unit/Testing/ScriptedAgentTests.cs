using FluentAssertions;
using MiyuAgents.Core;
using MiyuAgents.Testing;
using Xunit;

namespace MiyuAgents.Tests.Unit.Testing;

// El ScriptedAgent vive en la librería compartida MiyuAgents.Testing (no en este
// proyecto de tests).  Estos tests confirman su contrato — que el example y las
// suites de orquestación se apoyan en él para validar turnos sin LLM.

public class ScriptedAgent_TurnIndexing
{
    [Fact]
    public async Task Script_ReceivesIncrementingTurnIndexPerInstance()
    {
        var seen  = new List<int>();
        var agent = new ScriptedAgent("a", "A", (_, t) => { seen.Add(t); return $"r{t}"; });

        for (var i = 0; i < 3; i++)
            await agent.ProcessAsync(AgentContext.For("c", $"m{i}", "hi"), default);

        seen.Should().Equal(0, 1, 2);
        agent.TurnCount.Should().Be(3);
    }

    [Fact]
    public async Task Returns_ScriptedTextAsResponseData()
    {
        var agent = new ScriptedAgent("a", "A", (_, t) => $"turno-{t}");
        var r     = await agent.ProcessAsync(AgentContext.For("c", "m", "hi"), default);
        r.As<string>().Should().Be("turno-0");
    }

    [Fact]
    public async Task NullScriptResult_ProducesEmptyResponse()
    {
        var agent = new ScriptedAgent("a", "A", (_, _) => null);
        var r     = await agent.ProcessAsync(AgentContext.For("c", "m", "hi"), default);
        r.IsEmpty.Should().BeTrue();
    }
}

public class ScriptedAgent_Factories
{
    [Fact]
    public async Task Constant_AlwaysReturnsSameText()
    {
        var agent = ScriptedAgent.Constant("a", "A", "fijo");
        (await agent.ProcessAsync(AgentContext.For("c", "m0", "hi"), default)).As<string>().Should().Be("fijo");
        (await agent.ProcessAsync(AgentContext.For("c", "m1", "hi"), default)).As<string>().Should().Be("fijo");
    }

    [Fact]
    public async Task FromTurns_AdvancesThenClampsToLast()
    {
        var agent = ScriptedAgent.FromTurns("a", "A", "uno", "dos");

        (await agent.ProcessAsync(AgentContext.For("c", "m0", "hi"), default)).As<string>().Should().Be("uno");
        (await agent.ProcessAsync(AgentContext.For("c", "m1", "hi"), default)).As<string>().Should().Be("dos");
        // más allá del array: repite la última (el patrón que dispara el corte por repetición)
        (await agent.ProcessAsync(AgentContext.For("c", "m2", "hi"), default)).As<string>().Should().Be("dos");
    }

    [Fact]
    public void Role_DefaultsToConversation_AndIsOverridable()
    {
        ScriptedAgent.Constant("a", "A", "x").Role.Should().Be(AgentRole.Conversation);
        new ScriptedAgent("b", "B", (_, _) => "x", AgentRole.Analysis).Role.Should().Be(AgentRole.Analysis);
    }
}
