using MiyuAgents.Core;
using Microsoft.Extensions.Logging;

namespace MiyuAgents.Pipeline;

/// <summary>
/// Wraps a stage with a timeout. If the stage exceeds the timeout,
/// the pipeline is aborted with a timeout reason (not an exception).
/// </summary>
public sealed class TimedStage(
    IPipelineStage inner,
    TimeSpan timeout,
    ILogger? logger = null) : IPipelineStage
{
    public string StageName => inner.StageName;
    public int    Priority  => inner.Priority;

    public async Task<PipelineStageResult> ExecuteAsync(
        AgentContext ctx, PipelineContext pipeline, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            return await inner.ExecuteAsync(ctx, pipeline, cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // The timeout fired, not the caller's cancellation
            logger?.LogWarning("[{Stage}] timed out after {Timeout}ms", StageName, timeout.TotalMilliseconds);
            return PipelineStageResult.Abort(StageName, $"timeout after {timeout.TotalMilliseconds}ms");
        }
    }
}
