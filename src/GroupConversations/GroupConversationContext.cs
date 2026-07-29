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
    public IReadOnlyList<string>    ParticipantProfileIds { get; init; } = [];
    public required string          UserMessage         { get; init; }
    public string?                  OriginalFullMessage { get; init; }
    public IReadOnlyList<MediaAttachment> Attachments   { get; init; } = [];
    /// <summary>Compat: vista computada sobre <see cref="Attachments"/> (ver AgentContext).</summary>
    public byte[]?  ImageBytes     => Attachments.FirstOrDefault(a => a.Kind == AttachmentKind.Image)?.Bytes;
    public string?  ImageMediaType => Attachments.FirstOrDefault(a => a.Kind == AttachmentKind.Image)?.MediaType;
    public required IReadOnlyList<ConversationMessage> History { get; init; }
    public required bool            IsFirstTurn         { get; init; }
    public required string          Model               { get; init; }
    public ConversationMode         Mode                { get; init; } = ConversationMode.Normal;
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
            ParticipantProfileIds = ParticipantProfileIds,
            UserMessage         = UserMessage,
            OriginalFullMessage = OriginalFullMessage,
            Attachments         = Attachments,
            History             = History,
            IsFirstTurn         = IsFirstTurn,
            Model               = Model,
            Mode                = Mode,
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
            ParticipantProfileIds = baseCtx.ParticipantProfileIds,
            UserMessage         = baseCtx.UserMessage,
            OriginalFullMessage = baseCtx.OriginalFullMessage,
            Attachments         = baseCtx.Attachments,
            History             = baseCtx.History,
            IsFirstTurn         = baseCtx.IsFirstTurn,
            Model               = baseCtx.Model,
            Mode                = baseCtx.Mode,
            Metadata            = baseCtx.Metadata,
            Participants        = participants,
            Sender              = sender,
            GroupHistory        = groupHistory,
            AddressedToId       = addressedToId
        };
}
