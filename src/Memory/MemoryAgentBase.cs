using MiyuAgents.Core;
using Microsoft.Extensions.Logging;

namespace MiyuAgents.Memory;

public abstract class MemoryAgentBase<TQuery, TResult>(ILogger<MemoryAgentBase<TQuery, TResult>> logger)
    : AgentBase<TResult>(logger), IMemoryAgent<TQuery, TResult>
    where TQuery  : class
    where TResult : class
{
    protected abstract IMemoryStore<TQuery, TResult> Store { get; }
    protected abstract IEmbeddingProvider Embeddings { get; }

    public abstract MemoryKind MemoryKind { get; }

    public event AsyncEventHandler<MemoryRetrievedEventArgs<TResult>>? OnMemoryRetrieved;
    public event AsyncEventHandler<MemoryStoredEventArgs>?             OnMemoryStored;

    // AgentBase.ExecuteCoreAsync calls this
    protected override sealed async Task<TResult?> ExecuteCoreAsync(
        AgentContext ctx, CancellationToken ct)
    {
        // Step 1: Build typed query from context
        var query = await BuildQueryAsync(ctx, ct);
        if (query is null) return null;

        // Step 2: Retrieve from store
        var raw = await Store.SearchAsync(query, ct);

        // Step 3: Consumer-defined post-processing (decay, windowing, dedup)
        var result = await PostProcessAsync(raw, ctx, ct);

        // Step 4: Write to accumulator (consumer decides which list to populate)
        PopulateAccumulator(result, ctx);

        // Step 5: Fire event for observability / persistence
        await FireAsync(OnMemoryRetrieved, new MemoryRetrievedEventArgs<TResult>(
            AgentId, ctx.ConversationId, ctx.MessageId,
            result, IsEmpty(result), TimeSpan.Zero));

        return result;
    }

    /// <summary>
    /// Build the typed query. Return null to skip retrieval for this turn
    /// (e.g., ContinuityAgent returns null when it is not the first turn).
    /// </summary>
    protected abstract Task<TQuery?> BuildQueryAsync(AgentContext ctx, CancellationToken ct);

    /// <summary>
    /// Post-process raw store results: apply decay, deduplication, window logic, etc.
    /// Default implementation returns the raw results unchanged.
    /// </summary>
    protected virtual Task<TResult> PostProcessAsync(
        TResult raw, AgentContext ctx, CancellationToken ct) => Task.FromResult(raw);

    /// <summary>
    /// Write the typed result into the appropriate accumulator field.
    /// Each memory agent knows which list it belongs to.
    /// </summary>
    protected abstract void PopulateAccumulator(TResult result, AgentContext ctx);

    /// <summary>Check whether the result is considered empty (nothing to inject).</summary>
    protected abstract bool IsEmpty(TResult result);

    // Public RetrieveAsync and StoreAsync for direct callers (e.g., consolidation agent)
    public Task<TResult?> RetrieveAsync(TQuery query, AgentContext ctx, CancellationToken ct) =>
        Store.SearchAsync(query, ct)
             .ContinueWith(t => PostProcessAsync(t.Result, ctx, ct), ct)
             .Unwrap()
             .ContinueWith(t => (TResult?)t.Result, ct);

    public abstract Task StoreAsync(TResult data, AgentContext ctx, CancellationToken ct);
}