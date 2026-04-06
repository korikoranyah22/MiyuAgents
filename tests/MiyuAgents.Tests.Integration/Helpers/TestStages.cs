using MiyuAgents.Core;
using MiyuAgents.Pipeline;

namespace MiyuAgents.Tests.Integration.Helpers;

/// <summary>
/// Runs a single agent and stores its string result in ctx.Results.LlmResponse.
/// </summary>
internal sealed class AgentPipelineStage(string name, int priority, IAgent agent)
    : IPipelineStage
{
    public string StageName => name;
    public int    Priority  => priority;

    public async Task<PipelineStageResult> ExecuteAsync(
        AgentContext ctx, PipelineContext pipeline, CancellationToken ct)
    {
        var response = await agent.ProcessAsync(ctx, ct);
        if (response.As<string>() is { } text)
            ctx.Results.LlmResponse = text;
        return PipelineStageResult.Continue(name);
    }
}

/// <summary>
/// Throws for the first <paramref name="failCount"/> calls, then returns Continue.
/// Used to exercise RetryStage retry logic — unlike FlakyAgent, this throws at the
/// stage level so RetryStage actually retries.
/// </summary>
internal sealed class FlakyStage(string name, int priority, int failCount) : IPipelineStage
{
    private int _calls;
    public int CallCount => Volatile.Read(ref _calls);

    public string StageName => name;
    public int    Priority  => priority;

    public Task<PipelineStageResult> ExecuteAsync(
        AgentContext ctx, PipelineContext pipeline, CancellationToken ct)
    {
        if (Interlocked.Increment(ref _calls) <= failCount)
            throw new InvalidOperationException($"[{name}] planned stage failure #{_calls}");
        return Task.FromResult(PipelineStageResult.Continue(name));
    }
}

/// <summary>
/// Appends its stage name to a shared log and returns Continue.
/// Used to verify stage execution order or to detect stages that should not have run.
/// </summary>
internal sealed class TrackingStage(string name, int priority, IList<string> log)
    : IPipelineStage
{
    public int CallCount { get; private set; }

    public string StageName => name;
    public int    Priority  => priority;

    public Task<PipelineStageResult> ExecuteAsync(
        AgentContext ctx, PipelineContext pipeline, CancellationToken ct)
    {
        CallCount++;
        lock (log) log.Add(name);
        return Task.FromResult(PipelineStageResult.Continue(name));
    }
}
