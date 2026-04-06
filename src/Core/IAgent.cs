using MiyuAgents.Core.Events;

namespace MiyuAgents.Core;
/// <summary>
/// The minimum contract for any participant in a MiyuAgents system.
/// An agent receives a context, produces a typed response, and emits lifecycle events
/// that consumers can subscribe to for observability or persistence.
/// </summary>
public interface IAgent
{
    string    AgentId   { get; }
    string    AgentName { get; }
    AgentRole Role      { get; }

    // ── Lifecycle events ────────────────────────────────────────────────────
    // AsyncEventHandler allows subscribers to await (e.g., append to event store)
    event AsyncEventHandler<MessageReceivedEventArgs>       OnMessageReceived;
    event AsyncEventHandler<LlmCallRequestedEventArgs>      OnLLMCallRequested;
    event AsyncEventHandler<LlmCallRespondedEventArgs>      OnLLMCallResponded;
    event AsyncEventHandler<AgentResponseProducedEventArgs> OnResponseProduced;
    event AsyncEventHandler<AgentErrorEventArgs>            OnError;

    /// <summary>
    /// Process the current turn context and produce a response.
    /// An empty/null response is valid — agents may have nothing relevant for a given turn.
    /// </summary>
    Task<AgentResponse> ProcessAsync(AgentContext ctx, CancellationToken ct = default);
}

// Async delegate: allows awaiting in event handlers (e.g., Eventuous AppendToStreamAsync)
public delegate Task AsyncEventHandler<TArgs>(object sender, TArgs args);