using MiyuAgents.Core;
using MiyuAgents.Orchestration.Strategies;

namespace MiyuAgents.GroupConversations;

/// <summary>
/// Stateful orchestrator for N-to-M group conversations.
/// Manages participant lifecycle and routes messages to the right agents.
///
/// Distinct from IGroupOrchestrator (stateless, single-turn, N agents):
/// - IGroupOrchestrator: stateless, receives agents per call, multi-agent decision loop
/// - IGroupConversationOrchestrator: stateful, owns participants, full conversation lifecycle
/// </summary>
public interface IGroupConversationOrchestrator
{
    IReadOnlyList<IParticipant>             Participants { get; }
    IReadOnlyList<GroupConversationMessage> History      { get; }

    // ── Lifecycle events ────────────────────────────────────────────────────
    event AsyncEventHandler<GroupMessageProducedEventArgs>? OnMessageProduced;
    event AsyncEventHandler<ParticipantJoinedEventArgs>?    OnParticipantJoined;
    event AsyncEventHandler<ParticipantLeftEventArgs>?      OnParticipantLeft;

    // ── Participant management ───────────────────────────────────────────────
    Task AddParticipantAsync(IParticipant participant, CancellationToken ct = default);
    Task RemoveParticipantAsync(string participantId, CancellationToken ct = default);

    // ── Message routing ──────────────────────────────────────────────────────
    Task<GroupTurnResult> SendMessageAsync(
        GroupConversationMessage message, CancellationToken ct = default);
}

// ── Event args ──────────────────────────────────────────────────────────────

public sealed record GroupMessageProducedEventArgs(
    string                   ConversationId,
    GroupConversationMessage  Message
);

public sealed record ParticipantJoinedEventArgs(
    string       ConversationId,
    IParticipant Participant,
    DateTime     JoinedAt
);

public sealed record ParticipantLeftEventArgs(
    string       ConversationId,
    IParticipant Participant,
    DateTime     LeftAt
);

/// <summary>Result of a group conversation turn.</summary>
public sealed record GroupTurnResult(
    IReadOnlyList<GroupConversationMessage> ProducedMessages,
    IReadOnlyList<OrchestratorDecision>     Decisions,
    int                                     RoundsExecuted,
    TimeSpan                                TotalLatency
);
