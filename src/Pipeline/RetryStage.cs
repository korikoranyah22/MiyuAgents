using MiyuAgents.Core;
using Microsoft.Extensions.Logging;

namespace MiyuAgents.Pipeline;

/// <summary>
/// Wraps a stage with retry logic using exponential backoff.
/// Only retries on transient exceptions (not OperationCanceledException).
/// </summary>
public sealed class RetryStage(
    IPipelineStage inner,
    int maxAttempts = 3,
    TimeSpan? baseDelay = null,
    ILogger? logger = null) : IPipelineStage
{
    private readonly TimeSpan _baseDelay = baseDelay ?? TimeSpan.FromMilliseconds(200);

    public string StageName => inner.StageName;
    public int    Priority  => inner.Priority;

    public async Task<PipelineStageResult> ExecuteAsync(
        AgentContext ctx, PipelineContext pipeline, CancellationToken ct)
    {
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await inner.ExecuteAsync(ctx, pipeline, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                var delay = TimeSpan.FromMilliseconds(_baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
                logger?.LogWarning(ex, "[{Stage}] attempt {Attempt}/{Max} failed, retrying in {Delay}ms",
                    StageName, attempt, maxAttempts, delay.TotalMilliseconds);
                await Task.Delay(delay, ct);
            }
        }

        // Final attempt (no catch — let it propagate)
        return await inner.ExecuteAsync(ctx, pipeline, ct);
    }
}
