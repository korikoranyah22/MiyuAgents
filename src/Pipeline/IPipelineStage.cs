using MiyuAgents.Core;

namespace MiyuAgents.Pipeline;

/// <summary>
/// One step in the turn processing pipeline.
/// Stages are ordered by Priority (ascending) and executed sequentially by PipelineRunner.
///
/// A stage may:
///   - Read from AgentContext (immutable core) and ctx.Results (mutable accumulator)
///   - Write results to ctx.Results
///   - Emit events via pipeline.EventBus
///   - Return ShouldContinue = false to abort the chain
///
/// A stage must NOT:
///   - Hold conversation state between turns (stages are Singleton — they are stateless)
///   - Block the thread (always async)
///   - Suppress exceptions from agents without logging
/// </summary>
public interface IPipelineStage
{
    /// <summary>Human-readable name for logging and debug panels.</summary>
    string StageName { get; }

    /// <summary>Execution order. Lower runs first. Gaps are intentional (room for injection).</summary>
    int Priority { get; }

    Task<PipelineStageResult> ExecuteAsync(
        AgentContext    ctx,
        PipelineContext pipeline,
        CancellationToken ct);
}

public sealed record PipelineStageResult(
    bool      ShouldContinue,
    string    StageName,
    TimeSpan  Latency,
    string?   AbortReason = null,
    object?   StageData   = null
)
{
    /// <summary>Factory: stage completed successfully and the pipeline should continue.</summary>
    public static PipelineStageResult Continue(string stageName, string? note = null, object? data = null)
        => new(ShouldContinue: true,  StageName: stageName, Latency: TimeSpan.Zero, AbortReason: note,   StageData: data);

    /// <summary>Factory: stage decided the pipeline should stop (not an exception).</summary>
    public static PipelineStageResult Abort(string stageName, string reason, object? data = null)
        => new(ShouldContinue: false, StageName: stageName, Latency: TimeSpan.Zero, AbortReason: reason, StageData: data);
}