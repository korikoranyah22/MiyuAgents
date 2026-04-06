/// <summary>
/// Abstraction over any vector store (Qdrant, pgvector, Pinecone, in-memory...).
/// The framework has no knowledge of Qdrant types, collection names, or payload schemas.
/// The consumer implements this for each memory kind they need.
/// </summary>
public interface IMemoryStore<TQuery, TResult>
    where TQuery  : class
    where TResult : class
{
    /// <summary>Ensure the backing store is ready (create collections, indices, etc.).</summary>
    Task EnsureReadyAsync(CancellationToken ct = default);

    /// <summary>Search for relevant entries given a structured query.</summary>
    Task<TResult> SearchAsync(TQuery query, CancellationToken ct = default);

    /// <summary>Retrieve all entries for a given scope (e.g., all facts for a profile).</summary>
    Task<TResult> GetAllAsync(string scopeId, CancellationToken ct = default);

    /// <summary>Store a new entry. Returns the storage ID.</summary>
    Task<string> StoreAsync(object entry, CancellationToken ct = default);

    /// <summary>Upsert (overwrite if same ID). Returns the storage ID.</summary>
    Task<string> UpsertAsync(string id, object entry, CancellationToken ct = default);

    /// <summary>Delete an entry by ID.</summary>
    Task DeleteAsync(string id, CancellationToken ct = default);
}