using FluentAssertions;
using MiyuAgents.Pipeline;
using MiyuAgents.Tests.Unit.Fakes;
using Xunit;

namespace MiyuAgents.Tests.Unit.Pipeline;

// ── Succeeds on first attempt ─────────────────────────────────────────────────

public class RetryStage_SucceedsOnFirstAttempt : IAsyncLifetime
{
    private PipelineStageResult _result = default!;

    public async Task InitializeAsync()
    {
        var inner = FakePipelineStage.Tracking("inner", 10);
        var retry = new RetryStage(inner, maxAttempts: 3, baseDelay: TimeSpan.Zero);

        _result = await retry.ExecuteAsync(
            TestBuilders.Context(), TestBuilders.Pipeline(), CancellationToken.None);
    }

    [Fact] public void Result_ShouldContinue_IsTrue() => _result.ShouldContinue.Should().BeTrue();
    [Fact] public void StageName_IsInner()            => _result.StageName.Should().Be("inner");

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Theory: retry N times then succeed ───────────────────────────────────────

public class RetryStage_RetriesUntilSuccess
{
    // (failCount, maxAttempts, expectSuccess)
    public static TheoryData<int, int, bool> Cases => new()
    {
        { 0, 3, true  },   // success on first try
        { 1, 3, true  },   // 1 fail, then success
        { 2, 3, true  },   // 2 fails, then success on 3rd attempt
        { 3, 3, false },   // 3 fails = exhausted (max 3 means attempts 1,2,3)
        { 4, 3, false },   // more fails than max
    };

    [Theory, MemberData(nameof(Cases))]
    public async Task Retries_MatchExpectedOutcome(int failCount, int maxAttempts, bool expectSuccess)
    {
        var inner = FakePipelineStage.FailsNTimes("flaky", 10, failCount);
        var retry = new RetryStage(inner, maxAttempts, baseDelay: TimeSpan.Zero);

        var act = () => retry.ExecuteAsync(
            TestBuilders.Context(), TestBuilders.Pipeline(), CancellationToken.None);

        if (expectSuccess)
            (await act()).ShouldContinue.Should().BeTrue();
        else
            await act.Should().ThrowAsync<InvalidOperationException>();
    }
}

// ── Cancellation is not retried ───────────────────────────────────────────────

public class RetryStage_CancellationToken_NotRetried
{
    [Fact]
    public async Task OperationCanceled_PropagatesImmediately()
    {
        using var cts    = new CancellationTokenSource();
        var cancelStage  = new FakePipelineStage("cancel-stage", 10, (_, _, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(PipelineStageResult.Continue("cancel-stage"));
        });

        cts.Cancel();
        var retry = new RetryStage(cancelStage, maxAttempts: 5, baseDelay: TimeSpan.Zero);

        Func<Task> act = () => retry.ExecuteAsync(
            TestBuilders.Context(), TestBuilders.Pipeline(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}

// ── Priority and name delegate to inner ──────────────────────────────────────

public class RetryStage_DelegatesToInner_ForNameAndPriority
{
    [Fact]
    public void StageName_IsInnerStageName()
    {
        var inner = FakePipelineStage.Tracking("my-inner-stage", priority: 42);
        var retry = new RetryStage(inner);

        retry.StageName.Should().Be("my-inner-stage");
        retry.Priority.Should().Be(42);
    }
}
