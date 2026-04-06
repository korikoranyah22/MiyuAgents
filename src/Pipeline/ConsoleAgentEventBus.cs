namespace MiyuAgents.Pipeline;

/// <summary>
/// Writes events to the console as JSON-like strings.
/// Useful for development and debugging without a real event store.
/// </summary>
public sealed class ConsoleAgentEventBus : IAgentEventBus
{
    public Task PublishAsync<TEvent>(TEvent evt, CancellationToken ct = default)
        where TEvent : class
    {
        Console.WriteLine($"[EventBus] {typeof(TEvent).Name}: {System.Text.Json.JsonSerializer.Serialize(evt)}");
        return Task.CompletedTask;
    }
}
