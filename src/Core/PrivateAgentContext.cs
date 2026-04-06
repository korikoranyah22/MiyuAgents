using MiyuAgents.Pipeline;
using Microsoft.Extensions.Logging;

namespace MiyuAgents.Core;

/// <summary>
/// A scoped AgentContext for sub-agents of a composite agent.
/// Modifications to Results do not propagate to the parent context.
/// Events published go to a private bus, not the caller's bus.
/// </summary>
public static class PrivateAgentContext
{
    /// <summary>
    /// Creates a private copy of the context for sub-agent use.
    /// The copy shares immutable fields but has an isolated Results accumulator.
    /// </summary>
    public static (AgentContext PrivateCtx, IAgentEventBus PrivateBus) Create(
        AgentContext parentCtx,
        ILoggerFactory? loggerFactory = null)
    {
        var privateBus = new InMemoryAgentEventBus(loggerFactory);

        var privateCtx = parentCtx with
        {
            Results = new AgentContextAccumulator()   // isolated — sub-agent writes don't reach parent
        };

        // Store the private bus in metadata so sub-agents and stages can retrieve it
        privateCtx.Metadata["__private_event_bus"] = privateBus;

        return (privateCtx, privateBus);
    }

    public static IAgentEventBus? GetPrivateBus(AgentContext ctx) =>
        ctx.Metadata.TryGetValue("__private_event_bus", out var bus)
            ? bus as IAgentEventBus
            : null;
}