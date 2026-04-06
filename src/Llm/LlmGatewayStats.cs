namespace MiyuAgents.Llm;

/// <summary>
/// Thread-safe accumulator for LLM usage statistics.
/// One instance per gateway — never reset during application lifetime.
/// Exposes snapshots for debug panels and cost monitoring.
/// </summary>
public sealed class LlmGatewayStats
{
    private long _inputTokens;
    private long _outputTokens;
    private long _cacheHitTokens;
    private long _cacheMissTokens;
    private long _callCount;
    private long _errorCount;
    private long _streamedChunks;

    public long InputTokens      => Volatile.Read(ref _inputTokens);
    public long OutputTokens     => Volatile.Read(ref _outputTokens);
    public long CacheHitTokens   => Volatile.Read(ref _cacheHitTokens);
    public long CacheMissTokens  => Volatile.Read(ref _cacheMissTokens);
    public long CallCount        => Volatile.Read(ref _callCount);
    public long ErrorCount       => Volatile.Read(ref _errorCount);
    public long StreamedChunks   => Volatile.Read(ref _streamedChunks);

    public double CacheHitRate
    {
        get
        {
            var total = CacheHitTokens + CacheMissTokens;
            return total == 0 ? 0.0 : (double)CacheHitTokens / total;
        }
    }

    public void Record(LlmUsage usage)
    {
        Interlocked.Add(ref _inputTokens,    usage.InputTokens);
        Interlocked.Add(ref _outputTokens,   usage.OutputTokens);
        Interlocked.Add(ref _cacheHitTokens, usage.CacheHitTokens);
        Interlocked.Add(ref _cacheMissTokens, usage.CacheMissTokens);
        Interlocked.Increment(ref _callCount);
    }

    public void RecordChunk()        => Interlocked.Increment(ref _streamedChunks);
    public void RecordError()        => Interlocked.Increment(ref _errorCount);

    public LlmGatewayStatsSnapshot Snapshot() => new(
        ProviderName: "",   // set by router when aggregating
        InputTokens, OutputTokens, CacheHitTokens, CacheMissTokens,
        CacheHitRate, CallCount, ErrorCount, StreamedChunks);
}