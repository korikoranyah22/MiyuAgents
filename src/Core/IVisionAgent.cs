namespace MiyuAgents.Core;

/// <summary>
/// An agent that analyzes images attached to the conversation.
/// Populates ctx.Results.ImageDescription and ctx.Results.ImageIsSfw.
/// </summary>
public interface IVisionAgent : IAgent
{
    /// <summary>
    /// Describe the image in natural language.
    /// Returns null if no image is present in the context.
    /// </summary>
    Task<string?> DescribeAsync(AgentContext ctx, CancellationToken ct);
}
