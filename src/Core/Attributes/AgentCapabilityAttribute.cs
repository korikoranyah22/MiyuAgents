namespace MiyuAgents.Core.Attributes;

/// <summary>
/// Declares an agent's capabilities. Inspected by AgentRegistry at startup.
/// Must be placed on a class that implements IAgent.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AgentCapabilityAttribute : Attribute
{
    /// <summary>
    /// Semantic role identifier. Used for lookup by name, logging, and debug panels.
    /// Examples: "episodic-memory", "conversation", "vision-classifier"
    /// </summary>
    public required string Role { get; init; }

    /// <summary>Memory kinds this agent can read or write. Empty for non-memory agents.</summary>
    public MemoryKind[] MemoryAccess { get; init; } = [];

    /// <summary>
    /// True if this agent will call an LLM (consumes tokens).
    /// Used to estimate cost and display in debug panels.
    /// </summary>
    public bool CanInitiateLlmCalls { get; init; }

    /// <summary>
    /// Agents that must complete before this one runs.
    /// The pipeline runner uses this to determine execution order.
    /// </summary>
    public Type[] DependsOn { get; init; } = [];

    /// <summary>DI lifetime for this agent when registered via AgentRegistry.</summary>
    public AgentLifetime Lifetime { get; init; } = AgentLifetime.Singleton;
}

public enum AgentLifetime { Singleton, Scoped, Transient }