namespace MiyuAgents.GroupConversations;

/// <summary>
/// Routes messages to the appropriate participants.
/// Determines visibility: who sees each message.
/// Different from ITurnPolicy (who responds) — router controls who receives.
/// </summary>
public interface IParticipantRouter
{
    IReadOnlyList<string> Route(
        GroupConversationMessage        message,
        IReadOnlyList<IParticipant>     participants);
}

// ── Built-in routers ─────────────────────────────────────────────────────────

/// <summary>
/// Every participant sees every message.
/// Default for open group conversations.
/// </summary>
public sealed class BroadcastRouter : IParticipantRouter
{
    public IReadOnlyList<string> Route(
        GroupConversationMessage message,
        IReadOnlyList<IParticipant> participants) =>
        participants.Select(p => p.ParticipantId).ToList();
}

/// <summary>
/// Direct messages are visible only to sender and recipient.
/// Broadcast messages are visible to all.
/// </summary>
public sealed class DirectMessageRouter : IParticipantRouter
{
    public IReadOnlyList<string> Route(
        GroupConversationMessage message,
        IReadOnlyList<IParticipant> participants)
    {
        if (message.IsBroadcast)
            return participants.Select(p => p.ParticipantId).ToList();

        return [message.SenderId, message.AddressedToId!];
    }
}

/// <summary>
/// Escalates to all participants when the message contains urgent keywords.
/// Otherwise routes only to addressed participant or first agent.
/// </summary>
public sealed class EscalationRouter(IReadOnlyList<string>? urgentKeywords = null) : IParticipantRouter
{
    private readonly IReadOnlyList<string> _keywords = urgentKeywords ??
        ["urgent", "emergency", "critical", "help", "broken"];

    public IReadOnlyList<string> Route(
        GroupConversationMessage message,
        IReadOnlyList<IParticipant> participants)
    {
        var isUrgent = _keywords.Any(k =>
            message.Content.Contains(k, StringComparison.OrdinalIgnoreCase));

        if (isUrgent)
            return participants.Select(p => p.ParticipantId).ToList(); // escalate to all

        if (!message.IsBroadcast)
            return [message.SenderId, message.AddressedToId!];

        return participants.Select(p => p.ParticipantId).ToList();
    }
}

/// <summary>
/// Only routes to human participants — filters out agents.
/// Useful for human-to-human channels in mixed conversations.
/// </summary>
public sealed class HumanOnlyRouter : IParticipantRouter
{
    public IReadOnlyList<string> Route(
        GroupConversationMessage message,
        IReadOnlyList<IParticipant> participants) =>
        participants
            .Where(p => p.Kind == ParticipantKind.Human)
            .Select(p => p.ParticipantId)
            .ToList();
}
