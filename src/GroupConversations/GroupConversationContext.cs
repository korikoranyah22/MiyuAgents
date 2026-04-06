using MiyuAgents.Core;
using MiyuAgents.Llm;

namespace MiyuAgents.GroupConversations;

/// <summary>
/// A group-conversation-aware context that carries both the AgentContext
/// (for compatibility with IAgent.ProcessAsync) and the group state
/// (participants, group history, sender, addressing).
///
/// AgentContext is sealed, so this wraps it rather than inheriting.
/// Agents receive the inner AgentContext via the implicit conversion or .Base.
/// </summary>
public sealed record GroupConversationContext
{
    // ── Forwarded from AgentContext (immutable) ─────────────────────────────
    public required string          ConversationId      { get; init; }
    public required string          MessageId           { get; init; }
    public required string          ProfileId           { get; init; }
    public required string          CharacterId         { get; init; }
    public required string          UserMessage         { get; init; }
    public string?                  OriginalFullMessage { get; init; }
    public byte[]?                  ImageBytes          { get; init; }
    public string?                  ImageMediaType      { get; init; }
    public required IReadOnlyList<ConversationMessage> History { get; init; }
    public required bool            IsFirstTurn         { get; init; }
    public required string          Model               { get; init; }
    public IDictionary<string, object> Metadata         { get; init; } = new Dictionary<string, object>();

    // ── Group conversation state ────────────────────────────────────────────
    /// <summary>All current participants in the conversation.</summary>
    public required IReadOnlyList<IParticipant> Participants { get; init; }

    /// <summary>The participant who sent the message that triggered this turn.</summary>
    public required IParticipant Sender { get; init; }

    /// <summary>Full message history for the group conversation.</summary>
    public required IReadOnlyList<GroupConversationMessage> GroupHistory { get; init; }

    /// <summary>If set, the message was addressed to this specific participant.</summary>
    public string? AddressedToId { get; init; }

    // ── Mutable accumulator (compatible with AgentContext pattern) ──────────
    public AgentContextAccumulator Results { get; } = new();

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>True if this agent is directly addressed (or message is broadcast).</summary>
    public bool IsAddressedToMe(string agentId) =>
        AddressedToId is null || AddressedToId == agentId;

    /// <summary>All human participants.</summary>
    public IReadOnlyList<HumanParticipant> Humans =>
        Participants.OfType<HumanParticipant>().ToList();

    /// <summary>All agent participants.</summary>
    public IReadOnlyList<AgentParticipant> Agents =>
        Participants.OfType<AgentParticipant>().ToList();

    /// <summary>
    /// Convert to an AgentContext for use with standard IAgent.ProcessAsync.
    /// The Results accumulator is shared so agents can still write to it.
    /// </summary>
    public AgentContext ToAgentContext() =>
        new()
        {
            ConversationId      = ConversationId,
            MessageId           = MessageId,
            ProfileId           = ProfileId,
            CharacterId         = CharacterId,
            UserMessage         = UserMessage,
            OriginalFullMessage = OriginalFullMessage,
            ImageBytes          = ImageBytes,
            ImageMediaType      = ImageMediaType,
            History             = History,
            IsFirstTurn         = IsFirstTurn,
            Model               = Model,
            Metadata            = Metadata
        };

    // ── Factory ─────────────────────────────────────────────────────────────

    public static GroupConversationContext From(
        AgentContext baseCtx,
        IReadOnlyList<IParticipant> participants,
        IParticipant sender,
        IReadOnlyList<GroupConversationMessage> groupHistory,
        string? addressedToId = null) =>
        new()
        {
            ConversationId      = baseCtx.ConversationId,
            MessageId           = baseCtx.MessageId,
            ProfileId           = baseCtx.ProfileId,
            CharacterId         = baseCtx.CharacterId,
            UserMessage         = baseCtx.UserMessage,
            OriginalFullMessage = baseCtx.OriginalFullMessage,
            ImageBytes          = baseCtx.ImageBytes,
            ImageMediaType      = baseCtx.ImageMediaType,
            History             = baseCtx.History,
            IsFirstTurn         = baseCtx.IsFirstTurn,
            Model               = baseCtx.Model,
            Metadata            = baseCtx.Metadata,
            Participants        = participants,
            Sender              = sender,
            GroupHistory        = groupHistory,
            AddressedToId       = addressedToId
        };
}
