
namespace MiyuAgents.Pipeline;

/// <summary>
/// Infrastructure context for pipeline stages.
/// Distinct from AgentContext (which carries turn data).
/// PipelineContext carries: event bus, broadcast channel, stage history.
/// </summary>
public sealed class PipelineContext
{
    /// <summary>
    /// Bus for publishing lifecycle events to external observers (event stores, logging, metrics).
    /// Stages call pipeline.EventBus.PublishAsync(new SomeEvent(...)).
    /// The consumer wires this to Eventuous, MassTransit, or any other sink.
    /// </summary>
    public required IAgentEventBus EventBus { get; init; }

    /// <summary>
    /// Channel for broadcasting real-time updates to connected clients (e.g., SignalR).
    /// Stages that produce streaming output push chunks here.
    /// </summary>
    public required IRealtimeBroadcaster Broadcaster { get; init; }

    /// <summary>Results from stages already executed this turn. Useful for post-run analysis.</summary>
    public List<PipelineStageResult> StageHistory { get; } = [];

    /// <summary>
    /// Arbitrary key-value bag for stages to share transient data within a turn.
    /// Not persisted — use EventBus for persistence.
    /// </summary>
    public IDictionary<string, object> SharedData { get; } = new Dictionary<string, object>();
}