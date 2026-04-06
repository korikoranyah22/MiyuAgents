using MiyuAgents.Pipeline;
using MiyuAgents.Core;
using MiyuAgents.Llm;
using MiyuAgents.Orchestration.Strategies;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace MiyuAgents.Orchestration;

public sealed class DefaultGroupOrchestrator : IGroupOrchestrator
{
    private readonly IRoundDecisionStrategy            _strategy;
    private readonly AgentRegistry                     _registry;
    private readonly int                               _maxRounds;
    private readonly int                               _maxAgentsPerRound;
    private readonly ILogger<DefaultGroupOrchestrator> _logger;

    public DefaultGroupOrchestrator(
        IRoundDecisionStrategy strategy,
        AgentRegistry registry,
        ILogger<DefaultGroupOrchestrator> logger,
        int maxRounds = 5,
        int maxAgentsPerRound = 2)
    {
        _strategy          = strategy;
        _registry          = registry;
        _logger            = logger;
        _maxRounds         = maxRounds;
        _maxAgentsPerRound = maxAgentsPerRound;
    }

    public async Task<GroupTurnResult> RunTurnAsync(
        GroupMessage               userMessage,
        IReadOnlyList<GroupMessage> history,
        IReadOnlyList<IAgent>       agents,
        AgentContext                ctx,
        CancellationToken           ct)
    {
        var workingHistory = history.Concat([userMessage]).ToList();
        var produced       = new List<GroupMessage>();
        var decisions      = new List<OrchestratorDecision>();
        var sw             = Stopwatch.StartNew();
        var round          = 1;

        while (round <= _maxRounds)
        {
            var decision = await _strategy.DecideAsync(
                workingHistory, agents, round, _maxRounds, ctx, ct);
            decisions.Add(decision);

            if (decision.IsEmpty) break;

            // Cap agents per round
            var selectedIds = decision.SelectedAgentIds.Take(_maxAgentsPerRound);

            foreach (var agentId in selectedIds)
            {
                var agent = agents.FirstOrDefault(a => a.AgentId == agentId);
                if (agent is null)
                {
                    _logger.LogWarning("GroupOrchestrator: agent {AgentId} not found", agentId);
                    continue;
                }

                // Give each agent the full working history up to this point.
                // Each GroupMessage carries a Sender name which is passed as the native
                // ConversationMessage.Name so the LLM can distinguish speakers that share
                // the same role (e.g. two agents both writing as "assistant").
                var agentCtx = ctx with
                {
                    History = workingHistory
                        .Select(m => new ConversationMessage(m.Role, m.Content, m.Sender))
                        .ToList()
                };
                var response = await agent.ProcessAsync(agentCtx, ct);
                if (response.IsEmpty) continue;

                var msg = new GroupMessage(
                    Sender:    agent.AgentName,
                    Role:      "assistant",
                    Content:   response.As<string>() ?? response.Data?.ToString() ?? "",
                    Timestamp: DateTime.UtcNow);

                workingHistory.Add(msg);
                produced.Add(msg);
            }

            round++;
        }

        return new GroupTurnResult(produced, decisions, round - 1, sw.Elapsed);
    }
}