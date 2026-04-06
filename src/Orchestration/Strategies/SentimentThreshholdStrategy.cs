using MiyuAgents.Core;

namespace MiyuAgents.Orchestration.Strategies;

/// <summary>
/// Routes to different agents based on emotional state from EmotionalAnalysis.
/// Requires EmotionsBefore to be populated in ctx.Results before this strategy runs.
/// Falls back to a default agent if no emotional data is available.
/// </summary>
public sealed class SentimentThresholdStrategy(
    string defaultAgentId,
    string? highSadnessAgentId    = null,
    string? highJoyAgentId        = null,
    float   sadnessThreshold      = 0.7f,
    float   joyThreshold          = 0.6f) : IRoundDecisionStrategy
{
    public string StrategyName => "sentiment-threshold";

    public Task<OrchestratorDecision> DecideAsync(
        IReadOnlyList<GroupMessage> history,
        IReadOnlyList<IAgent> agents,
        int round, int maxRounds,
        AgentContext ctx, CancellationToken ct)
    {
        if (round > 1) return Task.FromResult(OrchestratorDecision.Empty(round, "done"));

        // Try to get emotional state from accumulator (typed as dynamic or cast)
        var emotions = ctx.Results.EmotionsBefore;

        if (emotions is not null)
        {
            // Use reflection to read Sadness/Joy properties if they exist
            var type    = emotions.GetType();
            var sadness = (float?)(type.GetProperty("Sadness")?.GetValue(emotions));
            var joy     = (float?)(type.GetProperty("Joy")?.GetValue(emotions));

            if (sadness > sadnessThreshold && highSadnessAgentId is not null)
                return Task.FromResult(new OrchestratorDecision(
                    [highSadnessAgentId], $"sentiment: high sadness ({sadness:F2})", round));

            if (joy > joyThreshold && highJoyAgentId is not null)
                return Task.FromResult(new OrchestratorDecision(
                    [highJoyAgentId], $"sentiment: high joy ({joy:F2})", round));
        }

        return Task.FromResult(new OrchestratorDecision(
            [defaultAgentId], "sentiment: default routing", round));
    }
}
