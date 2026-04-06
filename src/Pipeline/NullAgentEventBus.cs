namespace MiyuAgents.Pipeline;

/// <summary>
/// No-op event bus. Discards all events silently.
/// Use in tests or when observability is not needed.
/// </summary>
public sealed class NullAgentEventBus : IAgentEventBus
{
    public static readonly NullAgentEventBus Instance = new();
    public Task PublishAsync<TEvent>(TEvent evt, CancellationToken ct = default)
        where TEvent : class => Task.CompletedTask;
}
