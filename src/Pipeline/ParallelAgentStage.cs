using MiyuAgents.Core;
using System.Diagnostics;


namespace MiyuAgents.Pipeline;

/// <summary>
/// A stage that executes multiple agents in parallel (Task.WhenAll) and
/// collects all their results into ctx.Results.
///
/// Usage:
///   new ParallelAgentStage("MemoryRetrieval", priority: 200,
///       episodicAgent, factAgent, kbAgent);
/// </summary>
public sealed class ParallelAgentStage : IPipelineStage
{
    private readonly IReadOnlyList<IAgent> _agents;

    public string StageName { get; }
    public int    Priority  { get; }

    public ParallelAgentStage(string name, int priority, params IAgent[] agents)
    {
        StageName = name;
        Priority  = priority;
        _agents   = agents;
    }

    public async Task<PipelineStageResult> ExecuteAsync(
        AgentContext ctx, PipelineContext pipeline, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        var tasks = _agents.Select(a => a.ProcessAsync(ctx, ct)).ToArray();
        var results = await Task.WhenAll(tasks);

        // Publish aggregate event before checking for failures
        await pipeline.EventBus.PublishAsync(new AllAgentsInStageCompleted(
            StageName, ctx.ConversationId, ctx.MessageId,
            results.Select(r => r.AgentId).ToList()), ct);

        // Propagate agent failures — callers must see them, not silently swallow them
        var errors = results
            .Where(r => r.Status == AgentStatus.Error)
            .Select(r => new Exception($"[{r.AgentId}] {r.ErrorMessage}"))
            .ToList();

        if (errors.Count > 0)
            throw new AggregateException($"One or more agents failed in stage '{StageName}'.", errors);

        return new PipelineStageResult(true, StageName, sw.Elapsed);
    }
}

public record AllAgentsInStageCompleted(
    string StageName, string ConversationId, string MessageId,
    IReadOnlyList<string> AgentIds);