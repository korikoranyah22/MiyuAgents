namespace MiyuAgents.Pipeline;

/// <summary>
/// Async event bus for pipeline stages and agents.
/// The consumer wires this to their event store or logging infrastructure.
/// The framework does NOT provide a default implementation — it depends on your stack.
/// </summary>
public interface IAgentEventBus
{
    /// <summary>
    /// Publish an event. Consumers can append to Eventuous, publish to MassTransit, log, etc.
    /// </summary>
    Task PublishAsync<TEvent>(TEvent evt, CancellationToken ct = default)
        where TEvent : class;
}