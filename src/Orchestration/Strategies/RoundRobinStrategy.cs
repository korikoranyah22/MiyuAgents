using MiyuAgents.Core;

namespace MiyuAgents.Orchestration.Strategies;
public sealed class RoundRobinStrategy : IRoundDecisionStrategy
{
    public string StrategyName => "round-robin";

    public Task<OrchestratorDecision> DecideAsync(
        IReadOnlyList<GroupMessage> history,
        IReadOnlyList<IAgent> availableAgents,
        int currentRound, int maxRounds,
        AgentContext ctx, CancellationToken ct)
    {
        if (currentRound >= maxRounds || availableAgents.Count == 0)
            return Task.FromResult(OrchestratorDecision.Empty(currentRound, "max rounds"));

        var idx  = (currentRound - 1) % availableAgents.Count;
        var next = availableAgents[idx];
        return Task.FromResult(new OrchestratorDecision([next.AgentId], "round robin", currentRound));
    }
}