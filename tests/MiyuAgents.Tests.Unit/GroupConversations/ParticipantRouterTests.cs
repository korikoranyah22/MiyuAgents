using FluentAssertions;
using MiyuAgents.GroupConversations;
using MiyuAgents.Tests.Unit.Fakes;
using Xunit;

namespace MiyuAgents.Tests.Unit.GroupConversations;

// ── BroadcastRouter ───────────────────────────────────────────────────────────

public class BroadcastRouter_RoutesMessageToAll
{
    [Fact]
    public void Route_ReturnsAllParticipantIds()
    {
        IReadOnlyList<IParticipant> participants =
        [
            new HumanParticipant("u-1", "Alice"),
            new AgentParticipant("a-1", "Bot-A", new FakeAgent("a-1")),
            new AgentParticipant("a-2", "Bot-B", new FakeAgent("a-2")),
        ];
        var router  = new BroadcastRouter();
        var message = RouterTestHelpers.BroadcastMessage("conv-1", "u-1");

        var visible = router.Route(message, participants);

        visible.Should().BeEquivalentTo(["u-1", "a-1", "a-2"]);
    }
}

// ── DirectMessageRouter ───────────────────────────────────────────────────────

public class DirectMessageRouter_DirectMessage_OnlyVisibleToSenderAndRecipient
{
    [Fact]
    public void Route_DirectMessage_ReturnsSenderAndRecipient()
    {
        IReadOnlyList<IParticipant> participants =
        [
            new HumanParticipant("u-1", "Alice"),
            new AgentParticipant("a-1", "Bot-A", new FakeAgent("a-1")),
            new AgentParticipant("a-2", "Bot-B", new FakeAgent("a-2")),
        ];
        var router  = new DirectMessageRouter();
        var message = RouterTestHelpers.DirectMessage("conv-1", senderId: "u-1", recipientId: "a-1");

        var visible = router.Route(message, participants);

        visible.Should().BeEquivalentTo(["u-1", "a-1"]);
        visible.Should().NotContain("a-2");
    }
}

public class DirectMessageRouter_BroadcastMessage_VisibleToAll
{
    [Fact]
    public void Route_BroadcastMessage_ReturnsAll()
    {
        IReadOnlyList<IParticipant> participants =
        [
            new HumanParticipant("u-1", "Alice"),
            new AgentParticipant("a-1", "Bot",   new FakeAgent("a-1")),
        ];
        var router  = new DirectMessageRouter();
        var message = RouterTestHelpers.BroadcastMessage("conv-1", "u-1");

        var visible = router.Route(message, participants);

        visible.Should().HaveCount(2);
    }
}

// ── HumanOnlyRouter ───────────────────────────────────────────────────────────

public class HumanOnlyRouter_RoutesToHumansOnly
{
    [Fact]
    public void Route_ExcludesAgents()
    {
        IReadOnlyList<IParticipant> participants =
        [
            new HumanParticipant("u-1", "Alice"),
            new HumanParticipant("u-2", "Bob"),
            new AgentParticipant("a-1", "Bot", new FakeAgent("a-1")),
        ];
        var router  = new HumanOnlyRouter();
        var message = RouterTestHelpers.BroadcastMessage("conv-1", "a-1");

        var visible = router.Route(message, participants);

        visible.Should().BeEquivalentTo(["u-1", "u-2"]);
        visible.Should().NotContain("a-1");
    }
}

public class HumanOnlyRouter_NoHumans_ReturnsEmpty
{
    [Fact]
    public void Route_AllAgents_ReturnsEmptyList()
    {
        IReadOnlyList<IParticipant> participants =
        [
            new AgentParticipant("a-1", "Bot-A", new FakeAgent("a-1")),
            new AgentParticipant("a-2", "Bot-B", new FakeAgent("a-2")),
        ];
        var router  = new HumanOnlyRouter();
        var message = RouterTestHelpers.BroadcastMessage("conv-1", "a-1");

        var visible = router.Route(message, participants);

        visible.Should().BeEmpty();
    }
}

// ── EscalationRouter ─────────────────────────────────────────────────────────

public class EscalationRouter_UrgentKeyword_EscalatesToAll
{
    public static TheoryData<string> UrgentMessages => new()
    {
        { "this is urgent" },
        { "EMERGENCY in production" },
        { "critical failure detected" },
        { "please help us" },
        { "system is broken" },
    };

    [Theory, MemberData(nameof(UrgentMessages))]
    public void Route_UrgentContent_VisibleToAll(string content)
    {
        IReadOnlyList<IParticipant> participants =
        [
            new HumanParticipant("u-1", "Alice"),
            new AgentParticipant("a-1", "Bot", new FakeAgent("a-1")),
        ];
        var router  = new EscalationRouter();
        var message = RouterTestHelpers.BroadcastMessage("conv-1", "u-1", content);

        var visible = router.Route(message, participants);

        visible.Should().HaveCount(participants.Count);
    }
}

public class EscalationRouter_NonUrgentDirectMessage_RoutesNormally
{
    [Fact]
    public void Route_NonUrgentDirectMessage_OnlySenderAndRecipient()
    {
        IReadOnlyList<IParticipant> participants =
        [
            new HumanParticipant("u-1", "Alice"),
            new AgentParticipant("a-1", "Bot-A", new FakeAgent("a-1")),
            new AgentParticipant("a-2", "Bot-B", new FakeAgent("a-2")),
        ];
        var router  = new EscalationRouter();
        var message = RouterTestHelpers.DirectMessage("conv-1", senderId: "u-1", recipientId: "a-1", content: "hello there");

        var visible = router.Route(message, participants);

        visible.Should().BeEquivalentTo(["u-1", "a-1"]);
    }
}

// ── IParticipant types ────────────────────────────────────────────────────────

public class Participant_Kind_Properties
{
    [Fact]
    public void HumanParticipant_Kind_IsHuman()
    {
        var p = new HumanParticipant("h-1", "Alice");
        p.Kind.Should().Be(ParticipantKind.Human);
    }

    [Fact]
    public void AgentParticipant_Kind_IsAgent()
    {
        var p = new AgentParticipant("a-1", "Bot", new FakeAgent());
        p.Kind.Should().Be(ParticipantKind.Agent);
    }

    [Fact]
    public void HumanParticipant_RoleDescription_NullByDefault()
    {
        var p = new HumanParticipant("h-1", "Alice");
        p.RoleDescription.Should().BeNull();
    }
}

// ── File-scoped test helpers ──────────────────────────────────────────────────

file static class RouterTestHelpers
{
    public static GroupConversationMessage BroadcastMessage(
        string conversationId, string senderId, string content = "hello")
        => new()
        {
            MessageId      = Guid.NewGuid().ToString(),
            ConversationId = conversationId,
            SenderId       = senderId,
            SenderName     = "Sender",
            SenderKind     = ParticipantKind.Human,
            Content        = content,
            Role           = "user",
            Timestamp      = DateTime.UtcNow
            // AddressedToId = null → IsBroadcast = true
        };

    public static GroupConversationMessage DirectMessage(
        string conversationId, string senderId, string recipientId, string content = "hello")
        => new()
        {
            MessageId      = Guid.NewGuid().ToString(),
            ConversationId = conversationId,
            SenderId       = senderId,
            SenderName     = "Sender",
            SenderKind     = ParticipantKind.Human,
            Content        = content,
            Role           = "user",
            Timestamp      = DateTime.UtcNow,
            AddressedToId  = recipientId
        };
}
