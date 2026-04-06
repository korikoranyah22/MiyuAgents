namespace MiyuAgents.Memory;

/// <summary>
/// Decorator that adds LRU caching to any IMemoryStore.
/// Caches SearchAsync results by query key for a configurable duration.
/// Write operations (Store, Upsert, Delete) invalidate the entire cache.
/// </summary>
public sealed class CachingMemoryStore<TQuery, TResult>(
    IMemoryStore<TQuery, TResult> inner,
    int maxCacheEntries = 512,
    TimeSpan? ttl = null)
    : IMemoryStore<TQuery, TResult>
    where TQuery  : class, ICacheable
    where TResult : class
{
    private readonly TimeSpan _ttl = ttl ?? TimeSpan.FromMinutes(5);
    private readonly Dictionary<string, (TResult Result, DateTime ExpiresAt)> _cache = new();
    private readonly Queue<string> _evictionQueue = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public Task EnsureReadyAsync(CancellationToken ct = default) => inner.EnsureReadyAsync(ct);

    public async Task<TResult> SearchAsync(TQuery query, CancellationToken ct = default)
    {
        var key = query.CacheKey;

        await _lock.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(key, out var cached) && cached.ExpiresAt > DateTime.UtcNow)
                return cached.Result;
        }
        finally { _lock.Release(); }

        var result = await inner.SearchAsync(query, ct);

        await _lock.WaitAsync(ct);
        try
        {
            if (_cache.Count >= maxCacheEntries && _evictionQueue.TryDequeue(out var oldest))
                _cache.Remove(oldest);

            _cache[key] = (result, DateTime.UtcNow + _ttl);
            _evictionQueue.Enqueue(key);
        }
        finally { _lock.Release(); }

        return result;
    }

    public Task<TResult> GetAllAsync(string scopeId, CancellationToken ct = default) =>
        inner.GetAllAsync(scopeId, ct);

    public async Task<string> StoreAsync(object entry, CancellationToken ct = default)
    {
        await InvalidateCacheAsync();
        return await inner.StoreAsync(entry, ct);
    }

    public async Task<string> UpsertAsync(string id, object entry, CancellationToken ct = default)
    {
        await InvalidateCacheAsync();
        return await inner.UpsertAsync(id, entry, ct);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await InvalidateCacheAsync();
        await inner.DeleteAsync(id, ct);
    }

    private async Task InvalidateCacheAsync()
    {
        await _lock.WaitAsync();
        try { _cache.Clear(); _evictionQueue.Clear(); }
        finally { _lock.Release(); }
    }
}

/// <summary>
/// Query objects for CachingMemoryStore must provide a stable cache key.
/// </summary>
public interface ICacheable
{
    string CacheKey { get; }
}
