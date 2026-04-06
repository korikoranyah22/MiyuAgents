namespace MiyuAgents.Core;

/// <summary>
/// An agent that internally orchestrates sub-agents to produce its response.
/// Sub-agents are private: callers see only this agent's consolidated output.
///
/// The composite agent is responsible for:
/// - Building a private AgentContext for its sub-agents
/// - Deciding which sub-agents to invoke and in what order
/// - Preventing sub-agent events from leaking to the caller's event bus
/// - Merging sub-agent results into a single AgentResponse
///
/// Pattern: Composite (GoF) + Facade
/// </summary>
public interface ICompositeAgent : IAgent
{
    /// <summary>
    /// Sub-agents this composite coordinates.
    /// Exposed for introspection (debug panels, admin UIs) but not visible to callers.
    /// </summary>
    IReadOnlyList<IAgent> SubAgents { get; }
}