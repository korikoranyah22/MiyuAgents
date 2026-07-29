using Microsoft.Extensions.Logging;
using MiyuAgents.Core;
using MiyuAgents.Llm;
using System.Diagnostics;

namespace MiyuAgents.Pipeline;

/// <summary>
/// Executes a sequence of IPipelineStage in Priority order.
/// Stages are resolved from DI (all registered implementations of IPipelineStage).
///
/// When <see cref="CriticalPathCutoff"/> is set, stages with Priority &lt;= cutoff run
/// synchronously (the caller blocks until they complete). Stages above the cutoff are
/// fired in a background task so the caller can release locks and accept new work
/// immediately. This lets the user send the next message as soon as the LLM response
/// is delivered, while post-response work (memory storage, consolidation, fact
/// extraction) completes in the background.
/// </summary>
public sealed class PipelineRunner
{
    private readonly IReadOnlyList<IPipelineStage> _stages;
    private readonly ILogger<PipelineRunner> _logger;

    /// <summary>
    /// Stages with Priority &lt;= this value are on the critical path (awaited).
    /// Stages above this value run fire-and-forget in the background.
    /// When null (default), ALL stages are awaited (legacy behaviour).
    /// </summary>
    public int? CriticalPathCutoff { get; set; }

    public PipelineRunner(
        IEnumerable<IPipelineStage> stages,
        ILogger<PipelineRunner> logger)
    {
        // Order by Priority ascending; gap-friendly
        _stages = stages.OrderBy(s => s.Priority).ToList();
        _logger = logger;
    }

    public async Task<TurnResult> RunAsync(
        AgentContext    ctx,
        PipelineContext pipeline,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        // Partition stages into critical-path and post-response
        IEnumerable<IPipelineStage> criticalStages;
        IReadOnlyList<IPipelineStage>? postStages = null;

        if (CriticalPathCutoff.HasValue)
        {
            var cutoff = CriticalPathCutoff.Value;
            criticalStages = _stages.Where(s => s.Priority <= cutoff);
            postStages     = _stages.Where(s => s.Priority > cutoff).ToList();
        }
        else
        {
            criticalStages = _stages;
        }

        // ── Critical path: awaited, blocks caller ────────────────────────────
        var aborted = false;
        foreach (var stage in criticalStages)
        {
            _logger.LogDebug(
                "Stage [{Priority}] {Stage} — turn {MessageId}",
                stage.Priority, stage.StageName, ctx.MessageId);

            using var callScope = LlmCallScope.Push(new LlmCallMetadata(
                "conversation-pipeline", stage.StageName, "execute", ctx.MessageId));
            var stageResult = await stage.ExecuteAsync(ctx, pipeline, ct);
            pipeline.StageHistory.Add(stageResult);

            if (!stageResult.ShouldContinue)
            {
                _logger.LogInformation(
                    "Pipeline aborted at stage {Stage}: {Reason}",
                    stage.StageName, stageResult.AbortReason);
                aborted = true;
                break;
            }
        }

        // ── Post-response: fire-and-forget background work ───────────────────
        if (!aborted && postStages is { Count: > 0 })
        {
            _logger.LogDebug(
                "Dispatching {Count} post-response stages to background for turn {MessageId}",
                postStages.Count, ctx.MessageId);

            // Capture what we need — pipeline and ctx are reference types, safe to share
            // since the critical path is done writing to them.
            _ = Task.Run(async () =>
            {
                try
                {
                    foreach (var stage in postStages)
                    {
                        _logger.LogDebug(
                            "[Background] Stage [{Priority}] {Stage} — turn {MessageId}",
                            stage.Priority, stage.StageName, ctx.MessageId);

                        // Post-response stages get a long-lived token (not tied to caller)
                        using var bgCts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                        using var callScope = LlmCallScope.Push(new LlmCallMetadata(
                            "conversation-pipeline", stage.StageName, "execute", ctx.MessageId));
                        var stageResult = await stage.ExecuteAsync(ctx, pipeline, bgCts.Token);
                        pipeline.StageHistory.Add(stageResult);

                        if (!stageResult.ShouldContinue)
                        {
                            _logger.LogInformation(
                                "[Background] Pipeline aborted at post-response stage {Stage}: {Reason}",
                                stage.StageName, stageResult.AbortReason);
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[Background] Post-response pipeline failed for turn {MessageId}",
                        ctx.MessageId);
                }
            });
        }

        return new TurnResult(ctx, pipeline.StageHistory, sw.Elapsed);
    }
}
