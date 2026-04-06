using MiyuAgents.Core;

namespace MiyuAgents.Memory;
/// <summary>
/// Specialization of IAgent for memory operations.
/// TQuery: the type of query issued to the store (e.g., a struct with semantic + affective vectors)
/// TResult: the type of result returned (e.g., List&lt;EpisodicEntry&gt;)
/// </summary>
public interface IMemoryAgent<TQuery, TResult> : IAgent
    where TQuery  : class
    where TResult : class
{
    MemoryKind MemoryKind { get; }

    /// <summary>
    /// Retrieve memories relevant to the current turn.
    /// Called by the pipeline during the retrieval stage.
    /// </summary>
    Task<TResult?> RetrieveAsync(TQuery query, AgentContext ctx, CancellationToken ct = default);

    /// <summary>
    /// Store a memory produced by the current exchange.
    /// Called by the consolidation agent after the response is delivered.
    /// </summary>
    Task StoreAsync(TResult data, AgentContext ctx, CancellationToken ct = default);

    event AsyncEventHandler<MemoryRetrievedEventArgs<TResult>>? OnMemoryRetrieved;
    event AsyncEventHandler<MemoryStoredEventArgs>?             OnMemoryStored;
}