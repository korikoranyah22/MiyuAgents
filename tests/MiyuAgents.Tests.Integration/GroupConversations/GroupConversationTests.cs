using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MiyuAgents.GroupConversations;
using MiyuAgents.Tests.Integration.Helpers;
using Xunit;

namespace MiyuAgents.Tests.Integration.GroupConversations;

// ── BroadcastPolicy: all agents respond ──────────────────────────────────────

public class GroupConversation_BroadcastPolicy_AllAgentsRespond : IAsyncLifetime
{
    private DefaultGroupConversationOrchestrator _orchestrator = default!;
    private GroupTurnResult                      _result       = default!;

    public async Task InitializeAsync()
    {
        _orchestrator = new DefaultGroupConversationOrchestrator(
            turnPolicy:      new BroadcastTurnPolicy(),
            router:          new BroadcastRouter(),
            logger:          NullLogger<DefaultGroupConversationOrchestrator>.Instance,
            maxRoundsPerTurn: 1);   // 1 round: broadcast selects all agents once

        var human  = new HumanParticipant("user-1", "Alice");
        var agent1 = new AgentParticipant("agent-1", "Bot1", new EchoAgent("bot-1", "response from bot1"));
        var agent2 = new AgentParticipant("agent-2", "Bot2", new EchoAgent("bot-2", "response from bot2"));
        var agent3 = new AgentParticipant("agent-3", "Bot3", new EchoAgent("bot-3", "response from bot3"));

        await _orchestrator.AddParticipantAsync(human,  CancellationToken.None);
        await _orchestrator.AddParticipantAsync(agent1, CancellationToken.None);
        await _orchestrator.AddParticipantAsync(agent2, CancellationToken.None);
        await _orchestrator.AddParticipantAsync(agent3, CancellationToken.None);

        var msg = GroupConversationMessage.FromUser("conv-1", "user-1", "Alice", "hello everyone");
        _result = await _orchestrator.SendMessageAsync(msg, CancellationToken.None);
    }

    [Fact] public void ThreeResponses_WereProduced()
        => _result.ProducedMessages.Should().HaveCount(3);

    [Fact] public void History_HasFourEntries_OneUserPlusThreeAgents()
        => _orchestrator.History.Should().HaveCount(4);

    [Fact] public void AllResponses_AreFromAgents()
        => _result.ProducedMessages.Should().AllSatisfy(m => m.SenderKind.Should().Be(ParticipantKind.Agent));

    [Fact] public void AllThreeAgentIds_AreRepresented()
        => _result.ProducedMessages.Select(m => m.SenderId)
            .Should().BeEquivalentTo(["agent-1", "agent-2", "agent-3"]);

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── AddressedOnlyPolicy: only addressed agent responds ───────────────────────

public class GroupConversation_AddressedOnlyPolicy_OnlyAddressedAgentResponds : IAsyncLifetime
{
    private DefaultGroupConversationOrchestrator _orchestrator = default!;
    private GroupTurnResult                      _result       = default!;

    public async Task InitializeAsync()
    {
        _orchestrator = new DefaultGroupConversationOrchestrator(
            turnPolicy:      new AddressedOnlyPolicy(),
            router:          new BroadcastRouter(),
            logger:          NullLogger<DefaultGroupConversationOrchestrator>.Instance,
            maxRoundsPerTurn: 1);

        var human  = new HumanParticipant("user-1", "Alice");
        var agent1 = new AgentParticipant("agent-1", "Bot1", new EchoAgent("bot-1", "bot1 response"));
        var agent2 = new AgentParticipant("agent-2", "Bot2", new EchoAgent("bot-2", "bot2 response"));

        await _orchestrator.AddParticipantAsync(human,  CancellationToken.None);
        await _orchestrator.AddParticipantAsync(agent1, CancellationToken.None);
        await _orchestrator.AddParticipantAsync(agent2, CancellationToken.None);

        // Message addressed specifically to agent-1
        var msg = GroupConversationMessage.FromUser("conv-1", "user-1", "Alice", "hey bot1!", addressedToId: "agent-1");
        _result = await _orchestrator.SendMessageAsync(msg, CancellationToken.None);
    }

    [Fact] public void OnlyOneResponse_WasProduced()
        => _result.ProducedMessages.Should().HaveCount(1);

    [Fact] public void Response_IsFromAddressedAgent()
        => _result.ProducedMessages.Single().SenderId.Should().Be("agent-1");

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── History accumulates across multiple messages ──────────────────────────────

public class GroupConversation_MultipleMessages_HistoryAccumulates : IAsyncLifetime
{
    private DefaultGroupConversationOrchestrator _orchestrator = default!;

    public async Task InitializeAsync()
    {
        _orchestrator = new DefaultGroupConversationOrchestrator(
            turnPolicy:      new BroadcastTurnPolicy(),
            router:          new BroadcastRouter(),
            logger:          NullLogger<DefaultGroupConversationOrchestrator>.Instance,
            maxRoundsPerTurn: 1);

        var human = new HumanParticipant("user-1", "Alice");
        var agent = new AgentParticipant("agent-1", "Bot", new EchoAgent("bot-1", "pong"));

        await _orchestrator.AddParticipantAsync(human, CancellationToken.None);
        await _orchestrator.AddParticipantAsync(agent, CancellationToken.None);

        for (var i = 1; i <= 3; i++)
        {
            var msg = GroupConversationMessage.FromUser("conv-1", "user-1", "Alice", $"message {i}");
            await _orchestrator.SendMessageAsync(msg, CancellationToken.None);
        }
    }

    // 3 user messages + 3 agent responses = 6 total
    [Fact] public void History_HasSixEntries()
        => _orchestrator.History.Should().HaveCount(6);

    [Fact] public void History_StartsWithUserMessage()
        => _orchestrator.History.First().SenderKind.Should().Be(ParticipantKind.Human);

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── OnMessageProduced event fires for each response ──────────────────────────

public class GroupConversation_OnMessageProduced_FiredForEachResponse : IAsyncLifetime
{
    private readonly List<GroupConversationMessage> _firedMessages = [];

    public async Task InitializeAsync()
    {
        var orchestrator = new DefaultGroupConversationOrchestrator(
            turnPolicy:      new BroadcastTurnPolicy(),
            router:          new BroadcastRouter(),
            logger:          NullLogger<DefaultGroupConversationOrchestrator>.Instance,
            maxRoundsPerTurn: 1);

        var human  = new HumanParticipant("user-1", "Alice");
        var agent1 = new AgentParticipant("agent-1", "Bot1", new EchoAgent("bot-1", "r1"));
        var agent2 = new AgentParticipant("agent-2", "Bot2", new EchoAgent("bot-2", "r2"));

        await orchestrator.AddParticipantAsync(human,  CancellationToken.None);
        await orchestrator.AddParticipantAsync(agent1, CancellationToken.None);
        await orchestrator.AddParticipantAsync(agent2, CancellationToken.None);

        orchestrator.OnMessageProduced += (_, e) => { _firedMessages.Add(e.Message); return Task.CompletedTask; };

        var msg = GroupConversationMessage.FromUser("conv-1", "user-1", "Alice", "fire events");
        await orchestrator.SendMessageAsync(msg, CancellationToken.None);
    }

    [Fact] public void Event_WasFiredTwice()
        => _firedMessages.Should().HaveCount(2);

    [Fact] public void Event_ConversationIds_AreCorrect()
        => _firedMessages.Should().AllSatisfy(m => m.ConversationId.Should().Be("conv-1"));

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Remove participant: removed agent does not respond ────────────────────────

public class GroupConversation_AfterRemovingParticipant_AgentDoesNotRespond : IAsyncLifetime
{
    private DefaultGroupConversationOrchestrator _orchestrator = default!;
    private GroupTurnResult                      _result       = default!;

    public async Task InitializeAsync()
    {
        _orchestrator = new DefaultGroupConversationOrchestrator(
            turnPolicy:      new BroadcastTurnPolicy(),
            router:          new BroadcastRouter(),
            logger:          NullLogger<DefaultGroupConversationOrchestrator>.Instance,
            maxRoundsPerTurn: 1);

        var human  = new HumanParticipant("user-1", "Alice");
        var agent1 = new AgentParticipant("agent-1", "Bot1", new EchoAgent("bot-1", "still here"));
        var agent2 = new AgentParticipant("agent-2", "Bot2", new EchoAgent("bot-2", "removed"));

        await _orchestrator.AddParticipantAsync(human,  CancellationToken.None);
        await _orchestrator.AddParticipantAsync(agent1, CancellationToken.None);
        await _orchestrator.AddParticipantAsync(agent2, CancellationToken.None);

        // Remove agent-2 before sending the message
        await _orchestrator.RemoveParticipantAsync("agent-2", CancellationToken.None);

        var msg = GroupConversationMessage.FromUser("conv-1", "user-1", "Alice", "who's there?");
        _result = await _orchestrator.SendMessageAsync(msg, CancellationToken.None);
    }

    [Fact] public void OnlyOneResponse_WasProduced()
        => _result.ProducedMessages.Should().HaveCount(1);

    [Fact] public void RemainingAgent_Responded()
        => _result.ProducedMessages.Single().SenderId.Should().Be("agent-1");

    [Fact] public void RemovedAgent_IsNotInParticipants()
        => _orchestrator.Participants.Should().NotContain(p => p.ParticipantId == "agent-2");

    public Task DisposeAsync() => Task.CompletedTask;
}
