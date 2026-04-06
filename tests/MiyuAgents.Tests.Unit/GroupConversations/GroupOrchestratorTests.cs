using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MiyuAgents.GroupConversations;
using MiyuAgents.Tests.Unit.Fakes;
using Xunit;

namespace MiyuAgents.Tests.Unit.GroupConversations;

// ── AddParticipantAsync ───────────────────────────────────────────────────────

public class GroupOrchestrator_AddParticipant_AppearsInList : IAsyncLifetime
{
    private DefaultGroupConversationOrchestrator _orchestrator = default!;

    public async Task InitializeAsync()
    {
        _orchestrator = OrchestratorTestHelpers.BuildBroadcastOrchestrator();
        await _orchestrator.AddParticipantAsync(
            new HumanParticipant("u-1", "Alice"), CancellationToken.None);
    }

    [Fact] public void Participants_ContainsAddedParticipant()
        => _orchestrator.Participants.Should().ContainSingle(p => p.ParticipantId == "u-1");

    public Task DisposeAsync() => Task.CompletedTask;
}

public class GroupOrchestrator_AddParticipant_FiresJoinedEvent : IAsyncLifetime
{
    private ParticipantJoinedEventArgs? _event;

    public async Task InitializeAsync()
    {
        var orchestrator = OrchestratorTestHelpers.BuildBroadcastOrchestrator();
        orchestrator.OnParticipantJoined += (_, e) => { _event = e; return Task.CompletedTask; };

        await orchestrator.AddParticipantAsync(
            new HumanParticipant("u-1", "Alice"), CancellationToken.None);
    }

    [Fact] public void Event_WasFired()             => _event.Should().NotBeNull();
    [Fact] public void Event_Participant_IsCorrect() => _event!.Participant.ParticipantId.Should().Be("u-1");

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── RemoveParticipantAsync ────────────────────────────────────────────────────

public class GroupOrchestrator_RemoveParticipant_DisappearsFromList : IAsyncLifetime
{
    private DefaultGroupConversationOrchestrator _orchestrator = default!;

    public async Task InitializeAsync()
    {
        _orchestrator = OrchestratorTestHelpers.BuildBroadcastOrchestrator();
        await _orchestrator.AddParticipantAsync(new HumanParticipant("u-1", "Alice"), CancellationToken.None);
        await _orchestrator.AddParticipantAsync(new HumanParticipant("u-2", "Bob"),   CancellationToken.None);
        await _orchestrator.RemoveParticipantAsync("u-1", CancellationToken.None);
    }

    [Fact] public void RemovedParticipant_IsGone()
        => _orchestrator.Participants.Should().NotContain(p => p.ParticipantId == "u-1");

    [Fact] public void OtherParticipant_StillPresent()
        => _orchestrator.Participants.Should().Contain(p => p.ParticipantId == "u-2");

    public Task DisposeAsync() => Task.CompletedTask;
}

public class GroupOrchestrator_RemoveParticipant_FiresLeftEvent : IAsyncLifetime
{
    private ParticipantLeftEventArgs? _event;

    public async Task InitializeAsync()
    {
        var orchestrator = OrchestratorTestHelpers.BuildBroadcastOrchestrator();
        orchestrator.OnParticipantLeft += (_, e) => { _event = e; return Task.CompletedTask; };

        await orchestrator.AddParticipantAsync(new HumanParticipant("u-1", "Alice"), CancellationToken.None);
        await orchestrator.RemoveParticipantAsync("u-1", CancellationToken.None);
    }

    [Fact] public void Event_WasFired()             => _event.Should().NotBeNull();
    [Fact] public void Event_Participant_IsCorrect() => _event!.Participant.ParticipantId.Should().Be("u-1");

    public Task DisposeAsync() => Task.CompletedTask;
}

public class GroupOrchestrator_RemoveNonExistent_IsNoOp
{
    [Fact]
    public async Task RemoveUnknownId_DoesNotThrow()
    {
        var orchestrator = OrchestratorTestHelpers.BuildBroadcastOrchestrator();

        Func<Task> act = () => orchestrator.RemoveParticipantAsync("ghost-id", CancellationToken.None);
        await act.Should().NotThrowAsync();
    }
}

// ── SendMessageAsync ──────────────────────────────────────────────────────────

public class GroupOrchestrator_SendMessage_AddsToHistory : IAsyncLifetime
{
    private DefaultGroupConversationOrchestrator _orchestrator = default!;

    public async Task InitializeAsync()
    {
        _orchestrator = OrchestratorTestHelpers.BuildBroadcastOrchestrator();
        await _orchestrator.AddParticipantAsync(new HumanParticipant("u-1", "Alice"),   CancellationToken.None);
        await _orchestrator.AddParticipantAsync(
            new AgentParticipant("a-1", "Bot", FakeAgent.Returns("hi there")), CancellationToken.None);

        var message = GroupConversationMessage.FromUser("conv-1", "u-1", "Alice", "hello");
        await _orchestrator.SendMessageAsync(message, CancellationToken.None);
    }

    [Fact] public void History_ContainsOriginalMessage()
        => _orchestrator.History.Should().Contain(m => m.Content == "hello");

    public Task DisposeAsync() => Task.CompletedTask;
}

public class GroupOrchestrator_SendMessage_FiresProducedEvent : IAsyncLifetime
{
    private readonly List<GroupMessageProducedEventArgs> _events = [];

    public async Task InitializeAsync()
    {
        var orchestrator = OrchestratorTestHelpers.BuildBroadcastOrchestrator();
        orchestrator.OnMessageProduced += (_, e) => { _events.Add(e); return Task.CompletedTask; };

        await orchestrator.AddParticipantAsync(new HumanParticipant("u-1", "Alice"), CancellationToken.None);
        await orchestrator.AddParticipantAsync(
            new AgentParticipant("a-1", "Bot", FakeAgent.Returns("agent reply")), CancellationToken.None);

        await orchestrator.SendMessageAsync(
            GroupConversationMessage.FromUser("c", "u-1", "Alice", "go"),
            CancellationToken.None);
    }

    [Fact] public void ProducedEvent_WasFired()
        => _events.Should().NotBeEmpty();

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── GroupConversationMessage ──────────────────────────────────────────────────

public class GroupConversationMessage_FromUser_Properties
{
    [Fact]
    public void FromUser_SetsAllFields()
    {
        var msg = GroupConversationMessage.FromUser("conv-1", "u-1", "Alice", "hello world", "a-1");

        msg.ConversationId.Should().Be("conv-1");
        msg.SenderId.Should().Be("u-1");
        msg.SenderName.Should().Be("Alice");
        msg.Content.Should().Be("hello world");
        msg.AddressedToId.Should().Be("a-1");
        msg.SenderKind.Should().Be(ParticipantKind.Human);
        msg.Role.Should().Be("user");
        msg.IsBroadcast.Should().BeFalse();
    }

    [Fact]
    public void FromUser_WithoutAddress_IsBroadcast()
    {
        var msg = GroupConversationMessage.FromUser("c", "u-1", "Alice", "hi");
        msg.IsBroadcast.Should().BeTrue();
    }
}

// ── File-scoped helpers ───────────────────────────────────────────────────────

file static class OrchestratorTestHelpers
{
    public static DefaultGroupConversationOrchestrator BuildBroadcastOrchestrator(int maxRounds = 1)
        => new(
            new BroadcastTurnPolicy(),
            new BroadcastRouter(),
            NullLogger<DefaultGroupConversationOrchestrator>.Instance,
            maxRounds);
}
