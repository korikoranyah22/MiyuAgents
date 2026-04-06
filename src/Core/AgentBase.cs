using MiyuAgents.Core.Events;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using MiyuAgents.Llm;

namespace MiyuAgents.Core;
public abstract class AgentBase<TResult> : IAgent
{

    protected readonly ILogger _logger;

    public abstract string    AgentId   { get; }
    public abstract string    AgentName { get; }
    public abstract AgentRole Role      { get; }

    public event AsyncEventHandler<MessageReceivedEventArgs>?       OnMessageReceived;
    public event AsyncEventHandler<LlmCallRequestedEventArgs>?      OnLLMCallRequested;
    public event AsyncEventHandler<LlmCallRespondedEventArgs>?      OnLLMCallResponded;
    public event AsyncEventHandler<AgentResponseProducedEventArgs>? OnResponseProduced;
    public event AsyncEventHandler<AgentErrorEventArgs>?            OnError;

    public AgentBase(ILogger<AgentBase<TResult>> logger)
    {
        _logger = logger;
    }
    /// <summary>
    /// Template Method: flow is here, details are in ExecuteCoreAsync.
    /// Implementors never override ProcessAsync — they override ExecuteCoreAsync.
    /// </summary>
    public async Task<AgentResponse> ProcessAsync(AgentContext ctx, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        await FireAsync(OnMessageReceived,
            new MessageReceivedEventArgs(AgentId, ctx.ConversationId,
                ctx.MessageId, ctx.UserMessage));

        try
        {
            var data = await ExecuteCoreAsync(ctx, ct);
            sw.Stop();

            var response = AgentResponse.From(this, data, sw.Elapsed);

            await FireAsync(OnResponseProduced,
                new AgentResponseProducedEventArgs(AgentId, ctx.ConversationId,
                    ctx.MessageId, response));

            return response;
        }
        catch (OperationCanceledException)
        {
            throw;  // propagate cancellation without wrapping
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "[{AgentName}] error on turn {MessageId}",
                AgentName, ctx.MessageId);
            await FireAsync(OnError, new AgentErrorEventArgs(AgentId, ctx.MessageId, ex));
            return AgentResponse.Error(this, ex, sw.Elapsed);
        }
    }

    /// <summary>
    /// The agent's actual logic. Return null if there is nothing relevant for this turn.
    /// </summary>
    protected abstract Task<TResult?> ExecuteCoreAsync(AgentContext ctx, CancellationToken ct);

    // ── Helpers for implementors ─────────────────────────────────────────────

    protected Task FireLlmCallRequestedAsync(AgentContext ctx, string model, int estimatedTokens) =>
        FireAsync(OnLLMCallRequested, new LlmCallRequestedEventArgs(
            AgentId, ctx.ConversationId, ctx.MessageId, model, estimatedTokens, DateTime.UtcNow));

    protected Task FireLlmCallRespondedAsync(AgentContext ctx, LlmUsage usage, TimeSpan latency) =>
        FireAsync(OnLLMCallResponded, new LlmCallRespondedEventArgs(
            AgentId, ctx.ConversationId, ctx.MessageId, usage, latency, DateTime.UtcNow));

    protected async Task FireAsync<T>(AsyncEventHandler<T>? handler, T args)
    {
        if (handler is null) return;
        foreach (var del in handler.GetInvocationList().Cast<AsyncEventHandler<T>>())
            await del(this, args);
    }
}