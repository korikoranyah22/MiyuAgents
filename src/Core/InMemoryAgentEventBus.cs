using MiyuAgents.Pipeline;
using Microsoft.Extensions.Logging;

namespace MiyuAgents.Core;

/// <summary>
/// An IAgentEventBus that stores events in memory instead of persisting them.
/// Used for private sub-agent orchestration.
/// Optionally replays to the real bus when the composite agent completes,
/// allowing opt-in "reveal" of internal decisions for debugging.
/// </summary>
public sealed class InMemoryAgentEventBus : IAgentEventBus
{
    private readonly ILogger? _logger;
    private readonly List<object> _events = [];

    public InMemoryAgentEventBus(ILoggerFactory? loggerFactory = null)
    {
        _logger = loggerFactory?.CreateLogger<InMemoryAgentEventBus>();
    }

    public IReadOnlyList<object> CapturedEvents => _events;

    public Task PublishAsync<TEvent>(TEvent evt, CancellationToken ct = default)
        where TEvent : class
    {
        _events.Add(evt);
        _logger?.LogDebug("[PrivateBus] captured {EventType}", typeof(TEvent).Name);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Optionally replay captured events to a real bus (e.g., for debug mode).
    /// </summary>
    public async Task ReplayToAsync(IAgentEventBus realBus, CancellationToken ct = default)
    {
        foreach (var evt in _events)
        {
            var method = typeof(IAgentEventBus)
                .GetMethod(nameof(IAgentEventBus.PublishAsync))!
                .MakeGenericMethod(evt.GetType());
            await (Task)method.Invoke(realBus, [evt, ct])!;
        }
    }
}