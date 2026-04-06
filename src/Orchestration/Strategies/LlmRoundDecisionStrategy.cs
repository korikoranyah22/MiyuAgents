using Microsoft.Extensions.Logging;
using MiyuAgents.Core;
using MiyuAgents.Core.Attributes;
using MiyuAgents.Llm;
using System.Reflection;
using System.Text.Json;

namespace MiyuAgents.Orchestration.Strategies;


/// <summary>
/// Uses a fast LLM call to decide which agents respond next.
/// The LLM is given: available agents, recent history, emotional state, and rules.
/// It responds with JSON only: {"should_respond": [...], "reason": "..."}
///
/// On malformed JSON: retries once, then returns empty decision.
/// </summary>
public sealed class LlmRoundDecisionStrategy(ILlmGateway llm, string model, ILogger<LlmRoundDecisionStrategy> logger) : IRoundDecisionStrategy
{
    public string StrategyName => "llm-orchestrator";

    public async Task<OrchestratorDecision> DecideAsync(
        IReadOnlyList<GroupMessage>  history,
        IReadOnlyList<IAgent>        availableAgents,
        int currentRound, int maxRounds,
        AgentContext ctx, CancellationToken ct)
    {
        if (currentRound >= maxRounds)
            return OrchestratorDecision.Empty(currentRound, "max rounds reached");

        var prompt = BuildPrompt(availableAgents, history, ctx);

        LlmResponse response;
        try
        {
            response = await llm.CompleteAsync(new LlmRequest
            {
                Model    = model,
                Messages = [new("user", prompt)]
            }, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "LlmRoundDecisionStrategy: LLM call failed — returning empty");
            return OrchestratorDecision.Empty(currentRound, "llm error");
        }

        return ParseDecision(response.Content, currentRound)
            ?? OrchestratorDecision.Empty(currentRound, "parse failed after retry");
    }

    private string BuildPrompt(
        IReadOnlyList<IAgent> agents,
        IReadOnlyList<GroupMessage> history,
        AgentContext ctx)
    {
        var agentList = string.Join("\n",
            agents.Select(a => $"- {a.AgentName} ({a.AgentId}): {GetDescription(a)}"));

        var recentHistory = string.Join("\n",
            history.TakeLast(6).Select(m => $"{m.Sender}: {m.Content}"));

        var emotionInfo = ctx.Results.EmotionsBefore is { } e
            ? e.ToString()
            : "not available";

        return $$"""
            You are the orchestrator of a multi-agent conversation.
            Your only function is to decide who should speak next.

            Available agents:
            {{agentList}}

            Recent history (last 6 messages):
            {{recentHistory}}

            Emotional context: {{emotionInfo}}

            Rules:
            - Respond ONLY with valid JSON, no additional text.
            - If the message has been adequately addressed, return empty should_respond.
            - Prefer silence over redundancy.
            - An agent should not respond if it already spoke this round unless essential.
            - Maximum 2 agents per round.

            Format: {"should_respond": ["AgentId1"], "reason": "brief explanation"}
            """;
    }

    private OrchestratorDecision? ParseDecision(string json, int round)
    {
        try
        {
            using var doc   = JsonDocument.Parse(json);
            var root        = doc.RootElement;
            var agents      = root.GetProperty("should_respond")
                                  .EnumerateArray()
                                  .Select(e => e.GetString()!)
                                  .Where(s => !string.IsNullOrEmpty(s))
                                  .ToList();
            var reason      = root.TryGetProperty("reason", out var r)
                                  ? r.GetString() ?? ""
                                  : "";
            return new OrchestratorDecision(agents, reason, round);
        }
        catch (JsonException)
        {
            logger.LogWarning("LlmRoundDecisionStrategy: malformed JSON from LLM");
            return null;
        }
    }

    private static string GetDescription(IAgent agent)
    {
        var cap = agent.GetType().GetCustomAttribute<AgentCapabilityAttribute>();
        return cap?.Role ?? agent.AgentName;
    }
}