using MiyuAgents.Core;
using MiyuAgents.Llm;

namespace MiyuAgents.Orchestration.Strategies;

/// <summary>
/// Routes messages to agents based on detected topic.
/// Uses a lightweight LLM call to classify the topic, then maps to expert agents.
/// </summary>
public sealed class ExpertRoutingStrategy(
    Dictionary<string, IReadOnlyList<string>> topicToAgents,
    ILlmGateway llm,
    string classifierModel = "default") : IRoundDecisionStrategy
{
    public string StrategyName => "expert-routing";

    public async Task<OrchestratorDecision> DecideAsync(
        IReadOnlyList<GroupMessage> history,
        IReadOnlyList<IAgent> availableAgents,
        int currentRound, int maxRounds,
        AgentContext ctx, CancellationToken ct)
    {
        if (currentRound > 1) return OrchestratorDecision.Empty(currentRound, "expert already spoke");

        var topic = await DetectTopicAsync(ctx.UserMessage, ct);
        if (topicToAgents.TryGetValue(topic, out var expertIds))
        {
            var validIds = expertIds.Where(id => availableAgents.Any(a => a.AgentId == id)).ToList();
            if (validIds.Count > 0)
                return new OrchestratorDecision(validIds, $"expert routing: topic={topic}", currentRound);
        }

        return OrchestratorDecision.Empty(currentRound, $"no expert for topic '{topic}'");
    }

    private async Task<string> DetectTopicAsync(string message, CancellationToken ct)
    {
        var topics = string.Join(", ", topicToAgents.Keys);
        var response = await llm.CompleteAsync(new LlmRequest
        {
            Model        = classifierModel,
            SystemPrompt = $"Classify the message into one of these topics: {topics}. Return only the topic name, nothing else.",
            Messages     = [new ConversationMessage("user", message)]
        }, ct);

        var detected = response.Content.Trim().ToLowerInvariant();
        return topicToAgents.ContainsKey(detected) ? detected : "unknown";
    }
}
