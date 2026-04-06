namespace MiyuAgents.GroupConversations;

/// <summary>
/// A single message in a group conversation.
/// Can be from a human or an agent, broadcast or directed.
/// </summary>
public sealed class GroupConversationMessage
{
    public required string          MessageId      { get; init; }
    public required string          ConversationId { get; init; }
    public required string          SenderId       { get; init; }
    public required string          SenderName     { get; init; }
    public required ParticipantKind SenderKind     { get; init; }
    public required string          Content        { get; init; }
    public required string          Role           { get; init; }  // "user" | "assistant"
    public required DateTime        Timestamp      { get; init; }

    /// <summary>
    /// If set, only this participant should respond. Null = broadcast to all.
    /// </summary>
    public string? AddressedToId { get; init; }

    /// <summary>
    /// True when no specific recipient is addressed (broadcast).
    /// </summary>
    public bool IsBroadcast => AddressedToId is null;

    public IDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();

    /// <summary>Factory for a user message.</summary>
    public static GroupConversationMessage FromUser(
        string conversationId,
        string senderId,
        string senderName,
        string content,
        string? addressedToId = null) =>
        new()
        {
            MessageId      = Guid.NewGuid().ToString(),
            ConversationId = conversationId,
            SenderId       = senderId,
            SenderName     = senderName,
            SenderKind     = ParticipantKind.Human,
            Content        = content,
            Role           = "user",
            Timestamp      = DateTime.UtcNow,
            AddressedToId  = addressedToId
        };
}
