namespace MiyuAgents.Core;

/// <summary>
/// An orchestrator scoped to a single agent's private reasoning space.
/// Uses the same ITurnPolicy / IRoundDecisionStrategy as the group orchestrator,
/// but all execution is private to the owning agent.
/// </summary>
public interface IPrivateOrchestrator
{
    Task<AgentContext> RunPrivateRoundsAsync(
        AgentContext           privateCtx,
        IReadOnlyList<IAgent>  subAgents,
        int                    maxRounds,
        CancellationToken      ct = default);
}