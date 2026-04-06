using FluentAssertions;
using MiyuAgents.Core;
using MiyuAgents.Orchestration.Strategies;
using MiyuAgents.Tests.Unit.Fakes;
using Xunit;

namespace MiyuAgents.Tests.Unit.Orchestration;

// ── RoundRobinStrategy ────────────────────────────────────────────────────────

public class RoundRobinStrategy_SelectsAgentByRoundModulo
{
    private static readonly IReadOnlyList<IAgent> _agents = [
        new FakeAgent("agent-a", "A"),
        new FakeAgent("agent-b", "B"),
        new FakeAgent("agent-c", "C"),
    ];

    // (round, expectedAgentIndex)
    public static TheoryData<int, int> RoundCases => new()
    {
        { 1, 0 },   // round 1 → (1-1) % 3 = 0 → agent-a
        { 2, 1 },   // round 2 → (2-1) % 3 = 1 → agent-b
        { 3, 2 },   // round 3 → (3-1) % 3 = 2 → agent-c
        { 4, 0 },   // round 4 → (4-1) % 3 = 0 → wraps to agent-a
        { 5, 1 },   // round 5 → wraps to agent-b
    };

    [Theory, MemberData(nameof(RoundCases))]
    public async Task SelectsAgent_AtExpectedIndex(int round, int expectedIndex)
    {
        var strategy = new RoundRobinStrategy();
        var ctx      = AgentContext.For("c", "m", "hi");

        var decision = await strategy.DecideAsync([], _agents, round, maxRounds: 10, ctx, CancellationToken.None);

        decision.SelectedAgentIds.Should().ContainSingle()
            .Which.Should().Be(_agents[expectedIndex].AgentId);
    }

    [Fact]
    public async Task AtMaxRounds_ReturnsEmpty()
    {
        var strategy = new RoundRobinStrategy();
        var decision = await strategy.DecideAsync(
            [], _agents, currentRound: 5, maxRounds: 5,
            AgentContext.For("c", "m", "hi"), CancellationToken.None);

        decision.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task NoAgents_ReturnsEmpty()
    {
        var strategy = new RoundRobinStrategy();
        var decision = await strategy.DecideAsync(
            [], [], currentRound: 1, maxRounds: 5,
            AgentContext.For("c", "m", "hi"), CancellationToken.None);

        decision.IsEmpty.Should().BeTrue();
    }
}

// ── PriorityRoundStrategy ────────────────────────────────────────────────────

public class PriorityRoundStrategy_SelectsAgentByRegistrationOrder
{
    private static readonly IReadOnlyList<IAgent> _agents = [
        new FakeAgent("first",  "First"),
        new FakeAgent("second", "Second"),
        new FakeAgent("third",  "Third"),
    ];

    // (round, expectedAgentId)
    public static TheoryData<int, string> PriorityCases => new()
    {
        { 1, "first"  },
        { 2, "second" },
        { 3, "third"  },
    };

    [Theory, MemberData(nameof(PriorityCases))]
    public async Task SelectsAgent_AtRoundIndex(int round, string expectedId)
    {
        var strategy = new PriorityRoundStrategy();
        var decision = await strategy.DecideAsync(
            [], _agents, round, maxRounds: 10,
            AgentContext.For("c", "m", "hi"), CancellationToken.None);

        decision.SelectedAgentIds.Should().ContainSingle()
            .Which.Should().Be(expectedId);
    }

    [Fact]
    public async Task BeyondAgentCount_ReturnsEmpty()
    {
        var strategy = new PriorityRoundStrategy();
        var decision = await strategy.DecideAsync(
            [], _agents, currentRound: 4, maxRounds: 10,   // only 3 agents, round 4 > count
            AgentContext.For("c", "m", "hi"), CancellationToken.None);

        decision.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task AtMaxRounds_ReturnsEmpty()
    {
        var strategy = new PriorityRoundStrategy();
        var decision = await strategy.DecideAsync(
            [], _agents, currentRound: 3, maxRounds: 3,
            AgentContext.For("c", "m", "hi"), CancellationToken.None);

        decision.IsEmpty.Should().BeTrue();
    }
}

// ── OrchestratorDecision ─────────────────────────────────────────────────────

public class OrchestratorDecision_Properties
{
    [Fact]
    public void IsEmpty_True_WhenNoAgentsSelected()
    {
        var d = OrchestratorDecision.Empty(1, "test");
        d.IsEmpty.Should().BeTrue();
        d.SelectedAgentIds.Should().BeEmpty();
    }

    [Fact]
    public void IsEmpty_False_WhenAgentsSelected()
    {
        var d = new OrchestratorDecision(["agent-1"], "reason", 1);
        d.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Empty_Factory_SetsRoundAndReason()
    {
        var d = OrchestratorDecision.Empty(round: 5, reason: "max rounds");
        d.Round.Should().Be(5);
        d.Reason.Should().Be("max rounds");
    }
}
