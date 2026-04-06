using MiyuAgents.Core;
using MiyuAgents.Pipeline;


namespace MiyuAgents.Orchestration;

public interface ITurnOrchestrator
{
    Task<TurnResult> ExecuteTurnAsync(AgentContext ctx, CancellationToken ct = default);
}