
namespace MiyuAgents.Llm;
public sealed record LlmUsage(
    int InputTokens,
    int OutputTokens,
    int CacheHitTokens  = 0,
    int CacheMissTokens = 0,
    int ReasoningTokens = 0
)
{
    public static LlmUsage Zero => new(0, 0);
    public int TotalTokens => InputTokens + OutputTokens;
}