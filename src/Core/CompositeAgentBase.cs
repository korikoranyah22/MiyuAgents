using MiyuAgents.Pipeline;
using Microsoft.Extensions.Logging;

namespace MiyuAgents.Core;

/// <summary>
/// Base class for agents that orchestrate sub-agents privately.
///
/// The execution flow:
/// 1. Caller calls ProcessAsync(parentCtx)                    ← visible to group
/// 2. Base creates privateCtx with isolated bus + accumulator
/// 3. OrchestrateSubAgentsAsync(privateCtx) — your logic      ← private
/// 4. BuildResponseAsync(privateCtx) — merge results          ← private
/// 5. Returns consolidated AgentResponse                      ← visible to group
///
/// Optional: if DebugMode is true, private events are replayed to the caller's bus.
/// </summary>
public abstract class CompositeAgentBase<TResult> : AgentBase<TResult>, ICompositeAgent
{
    protected readonly ILoggerFactory _loggerFactory;
    protected abstract bool DebugMode { get; }

    public abstract IReadOnlyList<IAgent> SubAgents { get; }

    protected CompositeAgentBase(ILogger<AgentBase<TResult>> logger, ILoggerFactory loggerFactory)
        : base(logger)
    {
        _loggerFactory = loggerFactory;
    }

    protected override sealed async Task<TResult?> ExecuteCoreAsync(
        AgentContext parentCtx, CancellationToken ct)
    {
        var (privateCtx, privateBus) = PrivateAgentContext.Create(parentCtx, _loggerFactory);

        await OrchestrateSubAgentsAsync(privateCtx, ct);

        var result = await BuildResponseAsync(privateCtx, parentCtx, ct);

        if (DebugMode)
        {
            var callerBus = parentCtx.Metadata.TryGetValue("__pipeline_event_bus", out var b)
                ? b as IAgentEventBus : null;
            if (callerBus is not null && privateBus is InMemoryAgentEventBus inMemBus)
                await inMemBus.ReplayToAsync(callerBus, ct);
        }

        return result;
    }

    protected abstract Task OrchestrateSubAgentsAsync(
        AgentContext privateCtx, CancellationToken ct);

    protected abstract Task<TResult?> BuildResponseAsync(
        AgentContext privateCtx,
        AgentContext parentCtx,
        CancellationToken ct);
}