namespace MiyuAgents.Core;

/// <summary>
/// An agent that generates conversational responses via LLM.
/// Populates ctx.Results.LlmResponse and ctx.Results.TokenUsage.
/// </summary>
public interface IConversationAgent : IAgent
{
    /// <summary>
    /// Stream the agent's response token by token.
    /// Each chunk is broadcast via IRealTimeBroadcaster.
    /// </summary>
    IAsyncEnumerable<string> StreamResponseAsync(AgentContext ctx, CancellationToken ct);
}
