namespace MiyuAgents.Core;

/// <summary>
/// An agent that analyzes the conversation for emotional state, intent, or topics.
/// Populates ctx.Results.EmotionsBefore or ctx.Results.EmotionsAfter.
/// </summary>
public interface IAnalysisAgent : IAgent
{
    /// <summary>
    /// Analyze the current turn context and return a typed analysis result.
    /// </summary>
    Task<object?> AnalyzeAsync(AgentContext ctx, CancellationToken ct);
}
