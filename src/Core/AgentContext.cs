using MiyuAgents.Llm;

namespace MiyuAgents.Core;

/// <summary>
/// Immutable turn context. All identity fields are init-only.
/// Agents write results into ctx.Results (the mutable accumulator).
/// Agents must NOT create a new context — they receive and return the same instance.
/// </summary>
public sealed record AgentContext
{
    // ── Turn identity (immutable) ────────────────────────────────────────────
    public required string ConversationId { get; init; }
    public required string MessageId      { get; init; }
    public required string ProfileId      { get; init; }
    public required string CharacterId    { get; init; }

    // ── User input (immutable) ───────────────────────────────────────────────
    public required string UserMessage    { get; init; }

    /// <summary>
    /// When the message was fragmented from a long input, this is the full original text.
    /// Useful for memory retrieval agents that need the full semantic content.
    /// Null for normal messages.
    /// </summary>
    public string? OriginalFullMessage    { get; init; }

    /// <summary>
    /// Optional image bytes for vision-capable agents.
    /// Null when no image is attached.
    /// </summary>
    public byte[]?  ImageBytes    { get; init; }
    public string?  ImageMediaType { get; init; }

    // ── Conversation state (immutable snapshot) ──────────────────────────────
    public required IReadOnlyList<ConversationMessage> History { get; init; }
    public required bool IsFirstTurn { get; init; }

    // ── Runtime configuration (immutable) ────────────────────────────────────
    public required string Model          { get; init; }
    public IDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();

    // ── Mutable accumulator (agents write here) ──────────────────────────────
    public AgentContextAccumulator Results { get; init; } = new();

    // ── Factory ──────────────────────────────────────────────────────────────
    public static AgentContext For(
        string conversationId, string messageId, string userMessage,
        string model = "default") =>
        new()
        {
            ConversationId = conversationId,
            MessageId      = messageId,
            ProfileId      = "",
            CharacterId    = "",
            UserMessage    = userMessage,
            History        = [],
            IsFirstTurn    = true,
            Model          = model
        };
}