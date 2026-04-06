using MiyuAgents.Llm;
using MiyuAgents.Memory;

namespace MiyuAgents.Core;


/// <summary>
/// Mutable accumulator for results produced by agents during a turn.
/// Thread-safe for independent fields (agents running in parallel write to their own section).
/// </summary>
public sealed class AgentContextAccumulator
{
    // Memory agents populate these
    public List<object> EpisodicMemories  { get; } = [];
    public List<object> Facts             { get; } = [];
    public List<object> ContinuityChunks  { get; } = [];
    public List<object> KnowledgeChunks   { get; } = [];

    // Analysis agents populate these
    public object? EmotionsBefore   { get; set; }
    public object? EmotionsAfter    { get; set; }
    public object? AnalysisResult   { get; set; }

    // Vision agents populate these
    public string? ImageDescription { get; set; }
    public bool?   ImageIsSfw       { get; set; }

    // Conversation agent populates this
    public string?   LlmResponse  { get; set; }
    public LlmUsage? TokenUsage   { get; set; }

    // Arbitrary extra data any agent can store by key
    public IDictionary<string, object> Extra { get; } = new Dictionary<string, object>();

    internal ExchangeRecord ToExchangeRecord(AgentContext ctx, string profileName = "")
    {
        return new ExchangeRecord(
            ConversationId: ctx.ConversationId,
            MessageId:      ctx.MessageId,
            UserMessage:    ctx.UserMessage,
            AgentResponse:  LlmResponse ?? "",
            ProfileId:      ctx.ProfileId,
            CharacterId:    ctx.CharacterId,
            ProfileName:    profileName,
            FullHistory:    ctx.History,
            ExchangedAt:    DateTime.UtcNow
        );
    }
}