using MiyuAgents.Core;

namespace MiyuAgents.Pipeline;

/// <summary>
/// Aborts the pipeline if a required accumulator field is empty.
/// Useful as an early-exit guard after memory retrieval stages.
/// Example: abort if no LLM response was produced.
/// </summary>
public sealed class AbortIfEmptyStage(
    string stageName,
    int priority,
    Func<AgentContextAccumulator, bool> isEmpty,
    string abortReason) : IPipelineStage
{
    public string StageName => stageName;
    public int    Priority  => priority;

    public Task<PipelineStageResult> ExecuteAsync(
        AgentContext ctx, PipelineContext pipeline, CancellationToken ct)
    {
        if (isEmpty(ctx.Results))
        {
            return Task.FromResult(PipelineStageResult.Abort(stageName, abortReason));
        }
        return Task.FromResult(PipelineStageResult.Continue(stageName));
    }
}
