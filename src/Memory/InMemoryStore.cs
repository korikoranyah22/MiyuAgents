namespace MiyuAgents.Memory;

/// <summary>
/// An in-memory IMemoryStore that stores and searches entries without persistence.
/// Uses a simple linear scan for search — suitable for small collections and tests.
/// TQuery must implement IInMemoryQuery to define its own matching logic.
/// </summary>
public sealed class InMemoryStore<TQuery, TResult> : IMemoryStore<TQuery, TResult>
    where TQuery  : class, IInMemoryQuery<TResult>
    where TResult : class
{
    private readonly List<TResult> _entries = [];
    private readonly SemaphoreSlim _lock    = new(1, 1);

    public Task EnsureReadyAsync(CancellationToken ct = default) => Task.CompletedTask;

    public async Task<TResult> SearchAsync(TQuery query, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return query.Search(_entries);
        }
        finally { _lock.Release(); }
    }

    public async Task<TResult> GetAllAsync(string scopeId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            // Delegate scope filtering to the query contract via a scope query
            if (DefaultScopeQuery is { } scopeQuery)
                return scopeQuery(scopeId, _entries);

            throw new NotSupportedException(
                $"{nameof(InMemoryStore<TQuery, TResult>)} requires a ScopeQueryFactory to support GetAllAsync. " +
                "Use the overloaded constructor or derive a typed subclass.");
        }
        finally { _lock.Release(); }
    }

    public async Task<string> StoreAsync(object entry, CancellationToken ct = default)
    {
        if (entry is not TResult typed)
            throw new ArgumentException($"Expected {typeof(TResult).Name}, got {entry?.GetType().Name}.", nameof(entry));

        await _lock.WaitAsync(ct);
        try
        {
            _entries.Add(typed);
            return IdSelector?.Invoke(typed) ?? _entries.Count.ToString();
        }
        finally { _lock.Release(); }
    }

    public async Task<string> UpsertAsync(string id, object entry, CancellationToken ct = default)
    {
        if (entry is not TResult typed)
            throw new ArgumentException($"Expected {typeof(TResult).Name}, got {entry?.GetType().Name}.", nameof(entry));

        if (IdSelector is null)
            throw new InvalidOperationException($"{nameof(InMemoryStore<TQuery, TResult>)} requires an IdSelector to support UpsertAsync.");

        await _lock.WaitAsync(ct);
        try
        {
            var idx = _entries.FindIndex(e => IdSelector(e) == id);
            if (idx >= 0) _entries[idx] = typed;
            else          _entries.Add(typed);
            return id;
        }
        finally { _lock.Release(); }
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        if (IdSelector is null)
            throw new InvalidOperationException($"{nameof(InMemoryStore<TQuery, TResult>)} requires an IdSelector to support DeleteAsync.");

        await _lock.WaitAsync(ct);
        try { _entries.RemoveAll(e => IdSelector(e) == id); }
        finally { _lock.Release(); }
    }

    // ── Optional configuration ────────────────────────────────────────────────

    /// <summary>
    /// If set, used to extract the ID from an entry for Upsert and Delete.
    /// </summary>
    public Func<TResult, string>? IdSelector { get; init; }

    /// <summary>
    /// If set, used to serve GetAllAsync by scope ID without a typed query.
    /// </summary>
    public Func<string, IReadOnlyList<TResult>, TResult>? DefaultScopeQuery { get; init; }
}

/// <summary>
/// Query contract for InMemoryStore — implementors define their own search logic.
/// </summary>
public interface IInMemoryQuery<TResult>
{
    TResult Search(IReadOnlyList<TResult> entries);
}
