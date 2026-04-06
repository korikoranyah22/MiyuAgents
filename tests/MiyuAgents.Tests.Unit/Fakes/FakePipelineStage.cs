using MiyuAgents.Core;
using MiyuAgents.Pipeline;

namespace MiyuAgents.Tests.Unit.Fakes;

/// <summary>
/// Configurable IPipelineStage for unit testing.
/// Tracks how many times it was executed and in what order.
/// </summary>
public sealed class FakePipelineStage : IPipelineStage
{
    private readonly Func<AgentContext, PipelineContext, CancellationToken, Task<PipelineStageResult>> _executor;
    private int _callCount;

    public string StageName { get; }
    public int    Priority  { get; }
    public int    CallCount => _callCount;

    public FakePipelineStage(
        string stageName,
        int priority,
        Func<AgentContext, PipelineContext, CancellationToken, Task<PipelineStageResult>>? executor = null)
    {
        StageName = stageName;
        Priority  = priority;
        _executor = executor
            ?? ((_, _, _) => Task.FromResult(PipelineStageResult.Continue(stageName)));
    }

    public Task<PipelineStageResult> ExecuteAsync(
        AgentContext ctx, PipelineContext pipeline, CancellationToken ct)
    {
        Interlocked.Increment(ref _callCount);
        return _executor(ctx, pipeline, ct);
    }

    // ── Factory helpers ──────────────────────────────────────────────────────

    /// <summary>Stage that always continues and records its name into SharedData["order"].</summary>
    public static FakePipelineStage Tracking(string name, int priority)
        => new(name, priority, (_, pipeline, _) =>
        {
            if (!pipeline.SharedData.TryGetValue("order", out var raw))
                pipeline.SharedData["order"] = new List<string>();

            ((List<string>)pipeline.SharedData["order"]).Add(name);
            return Task.FromResult(PipelineStageResult.Continue(name));
        });

    /// <summary>Stage that always aborts with the given reason.</summary>
    public static FakePipelineStage Aborts(string name, int priority, string reason = "test abort")
        => new(name, priority, (_, _, _) =>
            Task.FromResult(PipelineStageResult.Abort(name, reason)));

    /// <summary>Stage that throws the given exception.</summary>
    public static FakePipelineStage Throws(string name, int priority, Exception ex)
        => new(name, priority, (_, _, _) => throw ex);

    /// <summary>Stage that fails N times then succeeds.</summary>
    public static FakePipelineStage FailsNTimes(string name, int priority, int failCount)
    {
        int attempts = 0;
        return new(name, priority, (_, _, _) =>
        {
            if (Interlocked.Increment(ref attempts) <= failCount)
                throw new InvalidOperationException($"Simulated failure #{attempts}");
            return Task.FromResult(PipelineStageResult.Continue(name));
        });
    }
}
