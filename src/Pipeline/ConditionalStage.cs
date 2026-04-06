using MiyuAgents.Core;

namespace MiyuAgents.Pipeline;

/// <summary>
/// Wraps another stage and only executes it if a condition is true.
/// Useful for feature flags or context-dependent stages.
/// Example: only run vision stage if an image is attached.
/// </summary>
public sealed class ConditionalStage(
    IPipelineStage inner,
    Func<AgentContext, bool> condition,
    string? skipReason = null) : IPipelineStage
{
    public string StageName => inner.StageName;
    public int    Priority  => inner.Priority;

    public Task<PipelineStageResult> ExecuteAsync(
        AgentContext ctx, PipelineContext pipeline, CancellationToken ct)
    {
        if (!condition(ctx))
            return Task.FromResult(PipelineStageResult.Continue(StageName,
                skipReason ?? $"condition false — {StageName} skipped"));

        return inner.ExecuteAsync(ctx, pipeline, ct);
    }
}
