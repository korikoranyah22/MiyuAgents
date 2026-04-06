using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MiyuAgents.Core;
using MiyuAgents.Orchestration;
using MiyuAgents.Orchestration.Strategies;
using MiyuAgents.Tests.Integration.Helpers;
using Xunit;

namespace MiyuAgents.Tests.Integration.Orchestration;

// ── PriorityRoundStrategy: agents respond once each in registration order ─────

public class GroupOrchestrator_PriorityStrategy_AgentsRespondInOrder : IAsyncLifetime
{
    private GroupTurnResult   _result = default!;
    private readonly List<string> _log = [];

    public async Task InitializeAsync()
    {
        var agents = new IAgent[]
        {
            new LoggingAgent("agent-a", "Alpha", _log),
            new LoggingAgent("agent-b", "Beta",  _log),
            new LoggingAgent("agent-c", "Gamma", _log),
        };

        // maxRounds=4: rounds 1,2,3 each select one agent; round 4 → strategy returns empty
        var orchestrator = new DefaultGroupOrchestrator(
            strategy:          new PriorityRoundStrategy(),
            registry:          new AgentRegistry(),
            logger:            NullLogger<DefaultGroupOrchestrator>.Instance,
            maxRounds:         4,
            maxAgentsPerRound: 2);

        var userMessage = new GroupMessage("User", "user", "hello agents", DateTime.UtcNow);
        var ctx         = AgentContext.For("conv-1", "msg-1", "hello agents");

        _result = await orchestrator.RunTurnAsync(userMessage, [], agents, ctx, CancellationToken.None);
    }

    [Fact] public void ThreeMessages_WereProduced()
        => _result.ProducedMessages.Should().HaveCount(3);

    [Fact] public void AgentsResponded_InRegistrationOrder()
        => _log.Should().Equal(["agent-a", "agent-b", "agent-c"]);

    [Fact] public void RoundsExecuted_IsThree()
        => _result.RoundsExecuted.Should().Be(3);

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── RoundRobinStrategy: cycles through agents ────────────────────────────────

public class GroupOrchestrator_RoundRobinStrategy_CyclesThroughAgents : IAsyncLifetime
{
    private GroupTurnResult   _result = default!;
    private readonly List<string> _log = [];

    public async Task InitializeAsync()
    {
        var agents = new IAgent[]
        {
            new LoggingAgent("agent-x", "X", _log),
            new LoggingAgent("agent-y", "Y", _log),
        };

        // maxRounds=5: rounds 1-4 produce responses; round 5 → strategy returns empty
        var orchestrator = new DefaultGroupOrchestrator(
            strategy:          new RoundRobinStrategy(),
            registry:          new AgentRegistry(),
            logger:            NullLogger<DefaultGroupOrchestrator>.Instance,
            maxRounds:         5,
            maxAgentsPerRound: 2);

        var userMessage = new GroupMessage("User", "user", "round robin test", DateTime.UtcNow);
        var ctx         = AgentContext.For("conv-2", "msg-2", "round robin test");

        _result = await orchestrator.RunTurnAsync(userMessage, [], agents, ctx, CancellationToken.None);
    }

    [Fact] public void FourMessages_WereProduced()
        => _result.ProducedMessages.Should().HaveCount(4);

    [Fact] public void EachAgent_RespondedTwice()
    {
        _log.Count(id => id == "agent-x").Should().Be(2);
        _log.Count(id => id == "agent-y").Should().Be(2);
    }

    [Fact] public void AgentsAlternated_XY_XY()
        => _log.Should().Equal(["agent-x", "agent-y", "agent-x", "agent-y"]);

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Empty agent list: zero messages produced ─────────────────────────────────

public class GroupOrchestrator_NoAgents_ProducesNoMessages
{
    [Fact]
    public async Task RunTurnAsync_WithEmptyAgentList_ReturnsEmptyResult()
    {
        var orchestrator = new DefaultGroupOrchestrator(
            strategy:  new RoundRobinStrategy(),
            registry:  new AgentRegistry(),
            logger:    NullLogger<DefaultGroupOrchestrator>.Instance,
            maxRounds: 3);

        var result = await orchestrator.RunTurnAsync(
            new GroupMessage("User", "user", "hello", DateTime.UtcNow),
            history: [],
            agents:  [],
            ctx:     AgentContext.For("c", "m", "hello"),
            ct:      CancellationToken.None);

        result.ProducedMessages.Should().BeEmpty();
    }
}

// ── History is passed to each round ──────────────────────────────────────────

public class GroupOrchestrator_WorkingHistory_AccumulatesAcrossRounds : IAsyncLifetime
{
    private GroupTurnResult _result = default!;

    public async Task InitializeAsync()
    {
        var agents = new IAgent[]
        {
            new EchoAgent("e-1", "response-1"),
            new EchoAgent("e-2", "response-2"),
        };

        var orchestrator = new DefaultGroupOrchestrator(
            strategy:  new PriorityRoundStrategy(),
            registry:  new AgentRegistry(),
            logger:    NullLogger<DefaultGroupOrchestrator>.Instance,
            maxRounds: 3);

        var priorHistory = new[]
        {
            new GroupMessage("User", "user", "prior message", DateTime.UtcNow)
        };

        var userMessage = new GroupMessage("User", "user", "new message", DateTime.UtcNow);
        var ctx         = AgentContext.For("c", "m", "new message");

        _result = await orchestrator.RunTurnAsync(userMessage, priorHistory, agents, ctx, CancellationToken.None);
    }

    [Fact] public void TwoMessages_WereProduced()
        => _result.ProducedMessages.Should().HaveCount(2);

    [Fact] public void TotalLatency_IsPositive()
        => _result.TotalLatency.Should().BeGreaterThan(TimeSpan.Zero);

    public Task DisposeAsync() => Task.CompletedTask;
}
