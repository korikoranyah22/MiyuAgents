using MiyuAgents.Core;
using Microsoft.Extensions.Logging;
using MiyuAgents.Core.Events;

namespace MiyuAgents.Memory;

public interface IConsolidationHandler
{
    string HandlerName { get; }
    Task<ConsolidationHandlerResult> HandleAsync(
        ExchangeRecord exchange, AgentContext ctx, CancellationToken ct);
}

public sealed class ConsolidationChain(IReadOnlyList<IConsolidationHandler> handlers, ILogger<ConsolidationChain> logger) : IConsolidationAgent
{
    private readonly IReadOnlyList<IConsolidationHandler> _handlers = handlers;
    private readonly ILogger<ConsolidationChain> _logger = logger;

    public string AgentId   => "consolidation-chain";
    public string AgentName => "ConsolidationChain";
    public AgentRole Role   => AgentRole.Memory;

    // IAgent events (required by interface, mostly unused for fire-and-forget)
    public event AsyncEventHandler<MessageReceivedEventArgs>?       OnMessageReceived       { add{} remove{} }
    public event AsyncEventHandler<LlmCallRequestedEventArgs>?      OnLLMCallRequested      { add{} remove{} }
    public event AsyncEventHandler<LlmCallRespondedEventArgs>?      OnLLMCallResponded      { add{} remove{} }
    public event AsyncEventHandler<AgentResponseProducedEventArgs>? OnResponseProduced      { add{} remove{} }
    public event AsyncEventHandler<AgentErrorEventArgs>?            OnError                 { add{} remove{} }

    public Task<AgentResponse> ProcessAsync(AgentContext ctx, CancellationToken ct) =>
        ConsolidateAsync(ctx.Results.ToExchangeRecord(ctx), ctx, ct)
            .ContinueWith(_ => AgentResponse.From(this, null, TimeSpan.Zero), ct);

    public async Task ConsolidateAsync(
        ExchangeRecord exchange, AgentContext ctx, CancellationToken ct)
    {
        foreach (var handler in _handlers)
        {
            try
            {
                await handler.HandleAsync(exchange, ctx, ct);
            }
            catch (Exception ex)
            {
                // One handler failing does not stop the chain
                _logger.LogError(ex, "ConsolidationHandler {Handler} failed for turn {MessageId}",
                    handler.HandlerName, exchange.MessageId);
            }
        }
    }
}

public record ConsolidationHandlerResult(
    string HandlerName, bool Succeeded, string? ErrorMessage = null);