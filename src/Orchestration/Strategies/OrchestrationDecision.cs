
namespace MiyuAgents.Orchestration.Strategies;

public sealed record OrchestratorDecision(
    IReadOnlyList<string> SelectedAgentIds,
    string  Reason,
    int     Round
)
{
    public bool IsEmpty => SelectedAgentIds.Count == 0;

    public static OrchestratorDecision Empty(int round, string reason) =>
        new([], reason, round);
}