using FluentAssertions;
using MiyuAgents.Llm;
using Xunit;

namespace MiyuAgents.Tests.Unit.Llm;

// ── Accumulation ─────────────────────────────────────────────────────────────

public class TokenTracker_Record_AccumulatesTokens : IAsyncLifetime
{
    private TokenTracker _tracker = default!;

    public Task InitializeAsync()
    {
        _tracker = new TokenTracker(128_000);
        _tracker.Record(new LlmUsage(InputTokens: 1_000, OutputTokens: 500));
        _tracker.Record(new LlmUsage(InputTokens: 2_000, OutputTokens: 750));
        return Task.CompletedTask;
    }

    [Fact] public void TotalInputTokens_IsSummed()  => _tracker.TotalInputTokens.Should().Be(3_000);
    [Fact] public void TotalOutputTokens_IsSummed() => _tracker.TotalOutputTokens.Should().Be(1_250);
    [Fact] public void TotalTokens_IsCombined()     => _tracker.TotalTokens.Should().Be(4_250);

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Context usage ratio ───────────────────────────────────────────────────────

public class TokenTracker_ContextUsageRatio
{
    public static TheoryData<int, int, int, double> Cases => new()
    {
        // window,  input,   output, expectedRatio
        { 128_000,  64_000,     0,   0.50  },
        { 100_000, 100_000,     0,   1.00  },
        { 128_000,       0,     0,   0.00  },
        {  10_000,   8_000,  2_000, 0.80  },  // ratio is based on input only
    };

    [Theory, MemberData(nameof(Cases))]
    public void Ratio_IsComputedFromInputTokensOnly(int window, int input, int output, double expectedRatio)
    {
        var tracker = new TokenTracker(window);
        tracker.Record(new LlmUsage(input, output));

        tracker.ContextUsageRatio.Should().BeApproximately(expectedRatio, precision: 0.0001);
    }

    [Fact]
    public void ZeroWindowSize_Ratio_IsZero()
    {
        var tracker = new TokenTracker(0);
        tracker.Record(new LlmUsage(1000, 500));
        tracker.ContextUsageRatio.Should().Be(0);
    }
}

// ── Soft / hard limits ───────────────────────────────────────────────────────

public class TokenTracker_Thresholds
{
    // window=100_000; input fills pct% of it
    public static TheoryData<int, bool, bool> ThresholdCases => new()
    {
        //  pct      soft   hard
        {  50_000, false, false },  // 50% — below both
        {  80_000,  true, false },  // 80% — at soft
        {  85_000,  true, false },  // 85% — above soft, below hard
        {  95_000,  true,  true },  // 95% — at hard
        { 100_000,  true,  true },  // 100% — above hard
    };

    [Theory, MemberData(nameof(ThresholdCases))]
    public void Limits_MatchExpected(int inputTokens, bool expectSoft, bool expectHard)
    {
        var tracker = new TokenTracker(100_000);
        tracker.Record(new LlmUsage(inputTokens, 0));

        tracker.IsApproachingSoftLimit().Should().Be(expectSoft, "soft limit check failed");
        tracker.IsApproachingHardLimit().Should().Be(expectHard, "hard limit check failed");
    }

    [Fact]
    public void CustomSoftThreshold_IsRespected()
    {
        // 60_000 / 100_000 = 60% usage ratio
        var tracker = new TokenTracker(100_000);
        tracker.Record(new LlmUsage(60_000, 0));

        tracker.IsApproachingSoftLimit(threshold: 0.50).Should().BeTrue();   // 60% >= 50% → true
        tracker.IsApproachingSoftLimit(threshold: 0.60).Should().BeTrue();   // 60% >= 60% → true
        tracker.IsApproachingSoftLimit(threshold: 0.65).Should().BeFalse();  // 60% <  65% → false
        tracker.IsApproachingSoftLimit(threshold: 0.70).Should().BeFalse();  // 60% <  70% → false
    }
}

// ── Snapshot immutability ────────────────────────────────────────────────────

public class TokenTracker_Snapshot_IsImmutable : IAsyncLifetime
{
    private TokenTracker         _tracker  = default!;
    private TokenTrackerSnapshot _snapshot = default!;

    public Task InitializeAsync()
    {
        _tracker  = new TokenTracker(128_000);
        _tracker.Record(new LlmUsage(1_000, 500));
        _snapshot = _tracker.Snapshot();

        // Record more AFTER taking snapshot
        _tracker.Record(new LlmUsage(5_000, 2_000));
        return Task.CompletedTask;
    }

    [Fact] public void Snapshot_TotalInput_NotMutated()  => _snapshot.TotalInputTokens.Should().Be(1_000);
    [Fact] public void Snapshot_TotalOutput_NotMutated() => _snapshot.TotalOutputTokens.Should().Be(500);
    [Fact] public void Tracker_StillUpdated()            => _tracker.TotalInputTokens.Should().Be(6_000);
    [Fact] public void Snapshot_WindowSize_Correct()     => _snapshot.ContextWindowSize.Should().Be(128_000);

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Thread safety ────────────────────────────────────────────────────────────

public class TokenTracker_ThreadSafety
{
    [Fact]
    public async Task ConcurrentRecords_ProduceCorrectTotal()
    {
        var tracker = new TokenTracker(10_000_000);
        const int threads = 100;
        const int tokensPerThread = 1_000;

        var tasks = Enumerable.Range(0, threads)
            .Select(_ => Task.Run(() =>
                tracker.Record(new LlmUsage(tokensPerThread, 0))));

        await Task.WhenAll(tasks);

        tracker.TotalInputTokens.Should().Be(threads * tokensPerThread);
    }
}
