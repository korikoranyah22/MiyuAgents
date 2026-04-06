namespace MiyuAgents.Llm;

/// <summary>
/// Tracks token usage for a single conversation over its lifetime.
/// Used to trigger context compression when approaching model limits.
/// </summary>
public sealed class TokenTracker
{
    private readonly int _contextWindowSize;
    private long _totalInputTokens;
    private long _totalOutputTokens;

    public TokenTracker(int contextWindowSize = 128_000)
    {
        _contextWindowSize = contextWindowSize;
    }

    public long TotalInputTokens  => Volatile.Read(ref _totalInputTokens);
    public long TotalOutputTokens => Volatile.Read(ref _totalOutputTokens);
    public long TotalTokens       => TotalInputTokens + TotalOutputTokens;

    public double ContextUsageRatio =>
        _contextWindowSize == 0 ? 0 : (double)TotalInputTokens / _contextWindowSize;

    public void Record(LlmUsage usage)
    {
        Interlocked.Add(ref _totalInputTokens,  usage.InputTokens);
        Interlocked.Add(ref _totalOutputTokens, usage.OutputTokens);
    }

    public bool IsApproachingSoftLimit(double threshold = 0.80) =>
        ContextUsageRatio >= threshold;

    public bool IsApproachingHardLimit(double threshold = 0.95) =>
        ContextUsageRatio >= threshold;

    public TokenTrackerSnapshot Snapshot() => new(
        TotalInputTokens, TotalOutputTokens,
        ContextUsageRatio, _contextWindowSize);
}

public sealed record TokenTrackerSnapshot(
    long TotalInputTokens, long TotalOutputTokens,
    double ContextUsageRatio, int ContextWindowSize);