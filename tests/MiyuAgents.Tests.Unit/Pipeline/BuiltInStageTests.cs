using FluentAssertions;
using MiyuAgents.Core;
using MiyuAgents.Pipeline;
using MiyuAgents.Tests.Unit.Fakes;
using Xunit;

namespace MiyuAgents.Tests.Unit.Pipeline;

// ── ConditionalStage ──────────────────────────────────────────────────────────

public class ConditionalStage_WhenTrue_ExecutesInner : IAsyncLifetime
{
    private PipelineStageResult _result     = default!;
    private FakePipelineStage   _innerStage = default!;

    public async Task InitializeAsync()
    {
        _innerStage = FakePipelineStage.Tracking("inner", 10);
        var conditional = new ConditionalStage(_innerStage, _ => true);

        _result = await conditional.ExecuteAsync(
            TestBuilders.Context(), TestBuilders.Pipeline(), CancellationToken.None);
    }

    [Fact] public void Result_ShouldContinue_IsTrue() => _result.ShouldContinue.Should().BeTrue();
    [Fact] public void InnerStage_WasExecuted()        => _innerStage.CallCount.Should().Be(1);

    public Task DisposeAsync() => Task.CompletedTask;
}

public class ConditionalStage_WhenFalse_SkipsInner : IAsyncLifetime
{
    private PipelineStageResult _result     = default!;
    private FakePipelineStage   _innerStage = default!;

    public async Task InitializeAsync()
    {
        _innerStage = FakePipelineStage.Tracking("inner", 10);
        var conditional = new ConditionalStage(_innerStage, _ => false, "condition false");

        _result = await conditional.ExecuteAsync(
            TestBuilders.Context(), TestBuilders.Pipeline(), CancellationToken.None);
    }

    [Fact] public void Result_ShouldContinue_IsTrue()  => _result.ShouldContinue.Should().BeTrue();  // skip ≠ abort
    [Fact] public void InnerStage_WasNotExecuted()      => _innerStage.CallCount.Should().Be(0);
    [Fact] public void AbortReason_ContainsSkipReason() => _result.AbortReason.Should().Be("condition false");

    public Task DisposeAsync() => Task.CompletedTask;
}

public class ConditionalStage_DelegatesToInner_ForMetadata
{
    [Fact]
    public void StageName_And_Priority_MatchInner()
    {
        var inner       = FakePipelineStage.Tracking("the-inner", priority: 77);
        var conditional = new ConditionalStage(inner, _ => true);

        conditional.StageName.Should().Be("the-inner");
        conditional.Priority.Should().Be(77);
    }
}

// ── AbortIfEmptyStage ─────────────────────────────────────────────────────────

public class AbortIfEmptyStage_WhenEmpty_Aborts : IAsyncLifetime
{
    private PipelineStageResult _result = default!;

    public async Task InitializeAsync()
    {
        // Condition: LlmResponse is null → empty
        var guard = new AbortIfEmptyStage(
            "response-guard", 10,
            isEmpty: acc => acc.LlmResponse is null,
            abortReason: "no LLM response");

        var ctx = TestBuilders.Context(); // ctx.Results.LlmResponse is null by default
        _result = await guard.ExecuteAsync(ctx, TestBuilders.Pipeline(), CancellationToken.None);
    }

    [Fact] public void ShouldContinue_IsFalse()  => _result.ShouldContinue.Should().BeFalse();
    [Fact] public void AbortReason_IsSet()        => _result.AbortReason.Should().Be("no LLM response");

    public Task DisposeAsync() => Task.CompletedTask;
}

public class AbortIfEmptyStage_WhenNotEmpty_Continues : IAsyncLifetime
{
    private PipelineStageResult _result = default!;

    public async Task InitializeAsync()
    {
        var guard = new AbortIfEmptyStage(
            "response-guard", 10,
            isEmpty: acc => acc.LlmResponse is null,
            abortReason: "no response");

        var ctx = TestBuilders.Context();
        ctx.Results.LlmResponse = "i exist";

        _result = await guard.ExecuteAsync(ctx, TestBuilders.Pipeline(), CancellationToken.None);
    }

    [Fact] public void ShouldContinue_IsTrue() => _result.ShouldContinue.Should().BeTrue();

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── TimedStage ────────────────────────────────────────────────────────────────

public class TimedStage_WithinTimeout_Succeeds : IAsyncLifetime
{
    private PipelineStageResult _result = default!;

    public async Task InitializeAsync()
    {
        var inner = FakePipelineStage.Tracking("fast-stage", 10);
        var timed = new TimedStage(inner, timeout: TimeSpan.FromSeconds(10));

        _result = await timed.ExecuteAsync(
            TestBuilders.Context(), TestBuilders.Pipeline(), CancellationToken.None);
    }

    [Fact] public void Result_ShouldContinue_IsTrue() => _result.ShouldContinue.Should().BeTrue();

    public Task DisposeAsync() => Task.CompletedTask;
}

public class TimedStage_ExceedsTimeout_Aborts : IAsyncLifetime
{
    private PipelineStageResult _result = default!;

    public async Task InitializeAsync()
    {
        var slowStage = new FakePipelineStage("slow-stage", 10,
            async (_, _, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10), ct); // will be cancelled by timeout
                return PipelineStageResult.Continue("slow-stage");
            });

        var timed = new TimedStage(slowStage, timeout: TimeSpan.FromMilliseconds(50));

        _result = await timed.ExecuteAsync(
            TestBuilders.Context(), TestBuilders.Pipeline(), CancellationToken.None);
    }

    [Fact] public void Result_ShouldContinue_IsFalse()    => _result.ShouldContinue.Should().BeFalse();
    [Fact] public void AbortReason_ContainsTimeout()       => _result.AbortReason.Should().Contain("timeout");

    public Task DisposeAsync() => Task.CompletedTask;
}

public class TimedStage_DelegatesToInner_ForMetadata
{
    [Fact]
    public void StageName_And_Priority_MatchInner()
    {
        var inner = FakePipelineStage.Tracking("timed-inner", priority: 55);
        var timed = new TimedStage(inner, timeout: TimeSpan.FromSeconds(1));

        timed.StageName.Should().Be("timed-inner");
        timed.Priority.Should().Be(55);
    }
}

// ── PipelineStageResult ───────────────────────────────────────────────────────

public class PipelineStageResult_FactoryMethods
{
    [Fact]
    public void Continue_ShouldContinue_IsTrue()
    {
        var r = PipelineStageResult.Continue("s");
        r.ShouldContinue.Should().BeTrue();
        r.StageName.Should().Be("s");
    }

    [Fact]
    public void Abort_ShouldContinue_IsFalse()
    {
        var r = PipelineStageResult.Abort("s", "reason");
        r.ShouldContinue.Should().BeFalse();
        r.AbortReason.Should().Be("reason");
    }

    [Fact]
    public void Continue_WithData_StoresData()
    {
        var r = PipelineStageResult.Continue("s", data: new { Value = 42 });
        r.StageData.Should().NotBeNull();
    }
}
