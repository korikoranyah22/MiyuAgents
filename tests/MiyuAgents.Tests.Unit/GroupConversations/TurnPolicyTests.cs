using FluentAssertions;
using MiyuAgents.Core;
using MiyuAgents.GroupConversations;
using MiyuAgents.Tests.Unit.Fakes;
using Xunit;

namespace MiyuAgents.Tests.Unit.GroupConversations;

// ── BroadcastTurnPolicy ───────────────────────────────────────────────────────

public class BroadcastTurnPolicy_SelectsAllAgents : IAsyncLifetime
{
    private TurnSelection _selection = default!;

    public async Task InitializeAsync()
    {
        var participants = PolicyTestHelpers.BuildParticipants();
        var policy       = new BroadcastTurnPolicy();
        var message      = PolicyTestHelpers.HumanMessage("hello everyone");
        var ctx          = PolicyTestHelpers.BuildGroupContext(message, participants);

        _selection = await policy.SelectRespondersAsync(message, participants, [], ctx, CancellationToken.None);
    }

    [Fact] public void Responders_ContainsBothAgents() => _selection.Responders.Should().HaveCount(2);
    [Fact] public void AllowConcurrent_IsTrue()         => _selection.AllowConcurrentResponses.Should().BeTrue();
    [Fact] public void Reason_MentionsBroadcast()       => _selection.Reason.Should().Contain("broadcast");

    public Task DisposeAsync() => Task.CompletedTask;
}

public class BroadcastTurnPolicy_ExcludesHumans : IAsyncLifetime
{
    private TurnSelection _selection = default!;

    public async Task InitializeAsync()
    {
        IReadOnlyList<IParticipant> participants =
        [
            new HumanParticipant("human-1", "Alice"),
            new AgentParticipant("agent-1", "Bot", new FakeAgent("agent-1")),
        ];
        var policy  = new BroadcastTurnPolicy();
        var message = PolicyTestHelpers.HumanMessage("hi");
        var ctx     = PolicyTestHelpers.BuildGroupContext(message, participants);

        _selection = await policy.SelectRespondersAsync(message, participants, [], ctx, CancellationToken.None);
    }

    [Fact] public void OnlyAgent_IsResponder()
        => _selection.Responders.Should().ContainSingle()
                                .Which.ParticipantId.Should().Be("agent-1");

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── AddressedOnlyPolicy ───────────────────────────────────────────────────────

public class AddressedOnlyPolicy_WithMention_SelectsTarget : IAsyncLifetime
{
    private TurnSelection _selection = default!;

    public async Task InitializeAsync()
    {
        var participants = PolicyTestHelpers.BuildParticipants();
        var policy       = new AddressedOnlyPolicy();
        var message      = PolicyTestHelpers.HumanMessage("hello", addressedToId: "agent-2");
        var ctx          = PolicyTestHelpers.BuildGroupContext(message, participants);

        _selection = await policy.SelectRespondersAsync(message, participants, [], ctx, CancellationToken.None);
    }

    [Fact] public void Responder_IsAddressedAgent()
        => _selection.Responders.Should().ContainSingle()
                                .Which.ParticipantId.Should().Be("agent-2");

    public Task DisposeAsync() => Task.CompletedTask;
}

public class AddressedOnlyPolicy_WithoutMention_SelectsNone : IAsyncLifetime
{
    private TurnSelection _selection = default!;

    public async Task InitializeAsync()
    {
        var participants = PolicyTestHelpers.BuildParticipants();
        var policy       = new AddressedOnlyPolicy();
        var message      = PolicyTestHelpers.HumanMessage("general message");
        var ctx          = PolicyTestHelpers.BuildGroupContext(message, participants);

        _selection = await policy.SelectRespondersAsync(message, participants, [], ctx, CancellationToken.None);
    }

    [Fact] public void Responders_IsEmpty()       => _selection.Responders.Should().BeEmpty();
    [Fact] public void Reason_MentionsNoAddress() => _selection.Reason.Should().Contain("@mention");

    public Task DisposeAsync() => Task.CompletedTask;
}

public class AddressedOnlyPolicy_UnknownAddressId_SelectsNone : IAsyncLifetime
{
    private TurnSelection _selection = default!;

    public async Task InitializeAsync()
    {
        var participants = PolicyTestHelpers.BuildParticipants();
        var policy       = new AddressedOnlyPolicy();
        var message      = PolicyTestHelpers.HumanMessage("hello", addressedToId: "ghost-id");
        var ctx          = PolicyTestHelpers.BuildGroupContext(message, participants);

        _selection = await policy.SelectRespondersAsync(message, participants, [], ctx, CancellationToken.None);
    }

    [Fact] public void Responders_IsEmpty() => _selection.Responders.Should().BeEmpty();

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── HumanOnlyPassthroughPolicy ────────────────────────────────────────────────

public class HumanOnlyPassthroughPolicy_HumanSender_AllAgentsRespond : IAsyncLifetime
{
    private TurnSelection _selection = default!;

    public async Task InitializeAsync()
    {
        var participants = PolicyTestHelpers.BuildParticipants();
        var policy       = new HumanOnlyPassthroughPolicy();
        var message      = PolicyTestHelpers.HumanMessage("hello");
        var ctx          = PolicyTestHelpers.BuildGroupContext(message, participants);

        _selection = await policy.SelectRespondersAsync(message, participants, [], ctx, CancellationToken.None);
    }

    [Fact] public void BothAgents_Respond() => _selection.Responders.Should().HaveCount(2);

    public Task DisposeAsync() => Task.CompletedTask;
}

public class HumanOnlyPassthroughPolicy_AgentSender_NoResponse : IAsyncLifetime
{
    private TurnSelection _selection = default!;

    public async Task InitializeAsync()
    {
        var participants = PolicyTestHelpers.BuildParticipants();
        var policy       = new HumanOnlyPassthroughPolicy();
        var message      = PolicyTestHelpers.AgentMessage("agent reply");
        var ctx          = PolicyTestHelpers.BuildGroupContext(message, participants);

        _selection = await policy.SelectRespondersAsync(message, participants, [], ctx, CancellationToken.None);
    }

    [Fact] public void Responders_IsEmpty()     => _selection.Responders.Should().BeEmpty();
    [Fact] public void Reason_MentionsPassthrough()
        => _selection.Reason.Should().Contain("agent-to-agent");

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── TurnSelection factory ─────────────────────────────────────────────────────

public class TurnSelection_None_CreatesEmptySelection
{
    [Fact]
    public void None_Responders_IsEmpty()
    {
        var s = TurnSelection.None("no reason");
        s.Responders.Should().BeEmpty();
        s.Reason.Should().Be("no reason");
    }
}

// ── Test helpers (file-scoped) ────────────────────────────────────────────────

file static class PolicyTestHelpers
{
    public static IReadOnlyList<IParticipant> BuildParticipants() =>
    [
        new HumanParticipant("user-1",  "Alice"),
        new AgentParticipant("agent-1", "Bot-A", new FakeAgent("agent-1")),
        new AgentParticipant("agent-2", "Bot-B", new FakeAgent("agent-2")),
    ];

    public static GroupConversationMessage HumanMessage(
        string content,
        string conversationId = "conv-1",
        string senderId       = "user-1",
        string senderName     = "Alice",
        string? addressedToId = null)
        => GroupConversationMessage.FromUser(conversationId, senderId, senderName, content, addressedToId);

    public static GroupConversationMessage AgentMessage(
        string content,
        string conversationId = "conv-1",
        string senderId       = "agent-1",
        string senderName     = "Bot")
        => new()
        {
            MessageId      = Guid.NewGuid().ToString(),
            ConversationId = conversationId,
            SenderId       = senderId,
            SenderName     = senderName,
            SenderKind     = ParticipantKind.Agent,
            Content        = content,
            Role           = "assistant",
            Timestamp      = DateTime.UtcNow
        };

    public static GroupConversationContext BuildGroupContext(
        GroupConversationMessage message,
        IReadOnlyList<IParticipant> participants)
    {
        var sender = participants.FirstOrDefault(p => p.ParticipantId == message.SenderId)
            ?? new HumanParticipant(message.SenderId, message.SenderName);

        return GroupConversationContext.From(
            AgentContext.For(message.ConversationId, message.MessageId, message.Content),
            participants,
            sender,
            [],
            message.AddressedToId);
    }
}
