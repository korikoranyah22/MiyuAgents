using MiyuAgents.Core;

namespace MiyuAgents.Orchestration.Strategies;

/// <summary>
/// Decides which agents should respond in the current round.
/// Implementations can use an LLM, a priority list, round-robin, or any other logic.
///
/// Strategy pattern: IGroupOrchestrator holds a reference to this interface.
/// The consumer selects the implementation via DI.
/// </summary>
public interface IRoundDecisionStrategy
{
    string StrategyName { get; }

    /// <summary>
    /// Given the current conversation state, decide which agents should speak next.
    /// Return an empty SelectedAgents list to end the turn.
    /// </summary>
    Task<OrchestratorDecision> DecideAsync(
        IReadOnlyList<GroupMessage>  history,
        IReadOnlyList<IAgent>        availableAgents,
        int                          currentRound,
        int                          maxRounds,
        AgentContext                 ctx,
        CancellationToken            ct = default);
}
