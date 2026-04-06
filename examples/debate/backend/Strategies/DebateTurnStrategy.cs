using MiyuAgents.Core;
using MiyuAgents.Orchestration;
using MiyuAgents.Orchestration.Strategies;

namespace Example.Strategies;

/// <summary>
/// Debate turn strategy for two agents + one human.
///
/// Each user turn produces up to 3 rounds:
///   Round 1 — agent that did NOT speak last in prior history opens
///   Round 2 — the other agent counter-argues
///   Round 3 — round-1 agent may offer an optional rebuttal
///              (returns null/IsEmpty → silently skipped if nothing to add)
///
/// Bug avoided: turn order (first/second) is decided ONCE at round 1 from the
/// history snapshot *before* any agent has spoken this turn, then cached in
/// ctx.Metadata. Subsequent rounds read the cache instead of re-evaluating
/// the growing workingHistory (which would flip the order mid-turn).
/// </summary>
public sealed class DebateTurnStrategy : IRoundDecisionStrategy
{
    public string StrategyName => "debate-turns";

    public Task<OrchestratorDecision> DecideAsync(
        IReadOnlyList<GroupMessage>  history,
        IReadOnlyList<IAgent>        availableAgents,
        int                          currentRound,
        int                          maxRounds,
        AgentContext                 ctx,
        CancellationToken            ct)
    {
        if (availableAgents.Count < 2)
            return Task.FromResult(OrchestratorDecision.Empty(currentRound, "need at least 2 agents"));

        var a0 = availableAgents[0];   // left-wing
        var a1 = availableAgents[1];   // right-wing

        if (currentRound == 1)
        {
            // At round 1, history = [...prior turns..., current user message].
            // We inspect only the prior turns (everything before the last item = user message)
            // to decide who opens, so intra-turn agent responses don't pollute this decision.
            var lastPriorAgentName = history
                .Take(Math.Max(0, history.Count - 1))
                .Where(m => m.Role == "assistant")
                .LastOrDefault()?.Sender;

            // first = whoever did NOT speak last; default to a0 on the very first turn
            string firstId  = lastPriorAgentName == a0.AgentName ? a1.AgentId : a0.AgentId;
            string secondId = firstId == a0.AgentId              ? a1.AgentId : a0.AgentId;

            // Cache so rounds 2 and 3 read a stable value even as workingHistory grows
            ctx.Metadata["debate_first"]  = firstId;
            ctx.Metadata["debate_second"] = secondId;
        }

        var first  = ctx.Metadata.TryGetValue("debate_first",  out var f) ? (string)f! : a0.AgentId;
        var second = ctx.Metadata.TryGetValue("debate_second", out var s) ? (string)s! : a1.AgentId;

        return currentRound switch
        {
            1 => Task.FromResult(
                     new OrchestratorDecision([first],  "opens turn",       currentRound)),

            2 => Task.FromResult(
                     new OrchestratorDecision([second], "counter-argument", currentRound)),

            3 => RebuttalDecision(first, ctx, currentRound),

            _ => Task.FromResult(OrchestratorDecision.Empty(currentRound, "debate complete"))
        };
    }

    private static Task<OrchestratorDecision> RebuttalDecision(
        string firstAgentId, AgentContext ctx, int round)
    {
        // Signal to PoliticalAgent that this is an optional slot.
        // The agent will respond with "SKIP" if it has nothing to add,
        // which causes ProcessAsync → IsEmpty → orchestrator silently skips.
        ctx.Metadata["debate_round"] = "rebuttal";
        return Task.FromResult(new OrchestratorDecision([firstAgentId], "optional rebuttal", round));
    }
}
