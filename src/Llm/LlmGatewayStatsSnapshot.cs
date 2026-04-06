namespace MiyuAgents.Llm;

public sealed record LlmGatewayStatsSnapshot(
    string ProviderName,
    long InputTokens, long OutputTokens,
    long CacheHitTokens, long CacheMissTokens,
    double CacheHitRate, long CallCount, long ErrorCount, long StreamedChunks);