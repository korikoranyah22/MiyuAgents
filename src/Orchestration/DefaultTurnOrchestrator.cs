using MiyuAgents.Pipeline;
using MiyuAgents.Core;

namespace MiyuAgents.Orchestration;

/// <summary>
/// Default orchestrator that wraps PipelineRunner.
/// Consumers can replace this with a custom implementation for session management,
/// locking, circuit routing, etc.
/// </summary>
public sealed class DefaultTurnOrchestrator(PipelineRunner runner, IAgentEventBus eventBus, IRealtimeBroadcaster broadcaster) 
: ITurnOrchestrator
{
    public async Task<TurnResult> ExecuteTurnAsync(AgentContext ctx, CancellationToken ct)
    {
        var pipeline = new PipelineContext
        {
            EventBus    = eventBus,
            Broadcaster = broadcaster
        };

        return await runner.RunAsync(ctx, pipeline, ct);
    }
}