using Microsoft.Extensions.Logging;
using MiyuAgents.Core;
using System.Diagnostics;

namespace MiyuAgents.Pipeline;

/// <summary>
/// Executes a sequence of IPipelineStage in Priority order.
/// Stages are resolved from DI (all registered implementations of IPipelineStage).
/// </summary>
public sealed class PipelineRunner
{
    private readonly IReadOnlyList<IPipelineStage> _stages;
    private readonly ILogger<PipelineRunner> _logger;

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

        foreach (var stage in _stages)
        {
            _logger.LogDebug(
                "Stage [{Priority}] {Stage} — turn {MessageId}",
                stage.Priority, stage.StageName, ctx.MessageId);

            var stageResult = await stage.ExecuteAsync(ctx, pipeline, ct);
            pipeline.StageHistory.Add(stageResult);

            if (!stageResult.ShouldContinue)
            {
                _logger.LogInformation(
                    "Pipeline aborted at stage {Stage}: {Reason}",
                    stage.StageName, stageResult.AbortReason);
                break;
            }
        }

        return new TurnResult(ctx, pipeline.StageHistory, sw.Elapsed);
    }
}