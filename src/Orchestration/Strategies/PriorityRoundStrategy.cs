using MiyuAgents.Core;

namespace MiyuAgents.Orchestration.Strategies;

/// <summary>
/// Selects agents by their registered order, one per round.
/// No LLM call needed — useful for testing and deterministic scenarios.
/// </summary>
public sealed class PriorityRoundStrategy : IRoundDecisionStrategy
{
    public string StrategyName => "priority";

    public Task<OrchestratorDecision> DecideAsync(
        IReadOnlyList<GroupMessage>  history,
        IReadOnlyList<IAgent>        availableAgents,
        int currentRound, int maxRounds,
        AgentContext ctx, CancellationToken ct)
    {
        if (currentRound > availableAgents.Count || currentRound >= maxRounds)
            return Task.FromResult(
                OrchestratorDecision.Empty(currentRound, "all agents responded"));

        var next = availableAgents[currentRound - 1];
        return Task.FromResult(
            new OrchestratorDecision([next.AgentId], "priority rotation", currentRound));
    }
}