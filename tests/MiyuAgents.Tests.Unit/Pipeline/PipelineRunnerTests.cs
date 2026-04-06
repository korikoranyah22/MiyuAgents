using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MiyuAgents.Pipeline;
using MiyuAgents.Tests.Unit.Fakes;
using Xunit;

namespace MiyuAgents.Tests.Unit.Pipeline;

// ── Stages run in priority order ─────────────────────────────────────────────

public class PipelineRunner_Run_ExecutesInPriorityOrder : IAsyncLifetime
{
    private TurnResult _result = default!;

    public async Task InitializeAsync()
    {
        // Intentionally out-of-order registration: 30, 10, 20
        var stages = new[]
        {
            FakePipelineStage.Tracking("stage-30", priority: 30),
            FakePipelineStage.Tracking("stage-10", priority: 10),
            FakePipelineStage.Tracking("stage-20", priority: 20),
        };

        var runner   = new PipelineRunner(stages, NullLogger<PipelineRunner>.Instance);
        var ctx      = TestBuilders.Context();
        var pipeline = TestBuilders.Pipeline();

        _result = await runner.RunAsync(ctx, pipeline, CancellationToken.None);
    }

    [Fact] public void StageHistory_HasThreeEntries() => _result.StageHistory.Should().HaveCount(3);

    [Fact] public void Execution_Order_IsAscendingByPriority()
    {
        var order = _result.StageHistory.Select(s => s.StageName).ToList();
        order.Should().ContainInOrder("stage-10", "stage-20", "stage-30");
    }

    [Fact] public void WasAborted_IsFalse() => _result.WasAborted.Should().BeFalse();

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Abort on ShouldContinue = false ──────────────────────────────────────────

public class PipelineRunner_Run_AbortsWhenStageSaysStop : IAsyncLifetime
{
    private TurnResult            _result  = default!;
    private FakePipelineStage     _stage3  = default!;

    public async Task InitializeAsync()
    {
        _stage3 = FakePipelineStage.Tracking("stage-3", priority: 30);

        var stages = new IPipelineStage[]
        {
            FakePipelineStage.Tracking("stage-1",   priority: 10),
            FakePipelineStage.Aborts(  "abort-gate", priority: 20, reason: "guard triggered"),
            _stage3,
        };

        var runner = new PipelineRunner(stages, NullLogger<PipelineRunner>.Instance);
        _result    = await runner.RunAsync(TestBuilders.Context(), TestBuilders.Pipeline(), CancellationToken.None);
    }

    [Fact] public void WasAborted_IsTrue()        => _result.WasAborted.Should().BeTrue();
    [Fact] public void Stage3_WasNotExecuted()    => _stage3.CallCount.Should().Be(0);
    [Fact] public void StageHistory_HasTwoEntries() => _result.StageHistory.Should().HaveCount(2);
    [Fact] public void AbortReason_IsPreserved()
        => _result.StageHistory[1].AbortReason.Should().Be("guard triggered");

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Empty stage list ──────────────────────────────────────────────────────────

public class PipelineRunner_Run_EmptyStages_NoThrow : IAsyncLifetime
{
    private TurnResult _result = default!;

    public async Task InitializeAsync()
    {
        var runner = new PipelineRunner([], NullLogger<PipelineRunner>.Instance);
        _result    = await runner.RunAsync(TestBuilders.Context(), TestBuilders.Pipeline(), CancellationToken.None);
    }

    [Fact] public void StageHistory_IsEmpty()    => _result.StageHistory.Should().BeEmpty();
    [Fact] public void WasAborted_IsFalse()      => _result.WasAborted.Should().BeFalse();
    [Fact] public void TotalLatency_IsPositive() => _result.TotalLatency.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── TurnResult.Response comes from accumulator ───────────────────────────────

public class PipelineRunner_Run_TurnResult_ResponseFromAccumulator : IAsyncLifetime
{
    private TurnResult _result = default!;

    public async Task InitializeAsync()
    {
        var writerStage = new FakePipelineStage("writer", 10,
            (ctx, _, _) =>
            {
                ctx.Results.LlmResponse = "pipeline answer";
                return Task.FromResult(MiyuAgents.Pipeline.PipelineStageResult.Continue("writer"));
            });

        var runner = new PipelineRunner([writerStage], NullLogger<PipelineRunner>.Instance);
        _result    = await runner.RunAsync(TestBuilders.Context(), TestBuilders.Pipeline(), CancellationToken.None);
    }

    [Fact] public void Response_IsFromAccumulator() => _result.Response.Should().Be("pipeline answer");

    public Task DisposeAsync() => Task.CompletedTask;
}
