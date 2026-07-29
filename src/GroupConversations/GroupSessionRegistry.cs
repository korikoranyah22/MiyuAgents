using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MiyuAgents.GroupConversations;

/// <summary>
/// Registro de sesiones en memoria con expiración por inactividad.  Thread-safe.
/// El reloj se inyecta vía <see cref="TimeProvider"/> para que los tests puedan
/// avanzar el tiempo sin esperas reales.
///
/// El callback <c>onEvicted</c> sólo se dispara en <see cref="SweepExpiredAsync"/>
/// (huérfanas), nunca en <see cref="Remove"/> (cierre limpio).  Si
/// <typeparamref name="TSession"/> es <see cref="IAsyncDisposable"/> o
/// <see cref="IDisposable"/>, se libera tras quitarla (en ambos caminos).
/// </summary>
public sealed class GroupSessionRegistry<TSession> : IGroupSessionRegistry<TSession>
    where TSession : class
{
    private sealed class Entry(TSession session, long lastAccessTicks)
    {
        public TSession Session { get; } = session;
        private long _lastAccessTicks = lastAccessTicks;
        public long LastAccessTicks => Interlocked.Read(ref _lastAccessTicks);
        public void Touch(long ticks) => Interlocked.Exchange(ref _lastAccessTicks, ticks);
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly TimeProvider _clock;
    private readonly TimeSpan _idleTtl;
    private readonly Func<string, TSession, CancellationToken, ValueTask>? _onEvicted;
    private readonly ILogger _logger;

    public GroupSessionRegistry(
        TimeSpan idleTtl,
        TimeProvider? timeProvider = null,
        Func<string, TSession, CancellationToken, ValueTask>? onEvicted = null,
        ILogger<GroupSessionRegistry<TSession>>? logger = null)
    {
        if (idleTtl <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(idleTtl), "El TTL de inactividad debe ser positivo.");
        _idleTtl   = idleTtl;
        _clock     = timeProvider ?? TimeProvider.System;
        _onEvicted = onEvicted;
        _logger    = logger ?? NullLogger<GroupSessionRegistry<TSession>>.Instance;
    }

    public int Count => _entries.Count;

    public IReadOnlyCollection<string> ActiveSessionIds => _entries.Keys.ToArray();

    public TSession GetOrCreate(string sessionId, Func<string, TSession> factory)
    {
        var now = NowTicks();
        var entry = _entries.GetOrAdd(sessionId, k =>
        {
            _logger.LogDebug("Sesión creada: {SessionId}", k);
            return new Entry(factory(k), now);
        });
        entry.Touch(now);
        return entry.Session;
    }

    public bool TryGet(string sessionId, out TSession? session)
    {
        if (_entries.TryGetValue(sessionId, out var entry))
        {
            entry.Touch(NowTicks());
            session = entry.Session;
            return true;
        }
        session = null;
        return false;
    }

    public bool Remove(string sessionId)
    {
        if (!_entries.TryRemove(sessionId, out var entry)) return false;
        _logger.LogDebug("Sesión cerrada (limpio): {SessionId}", sessionId);
        // Cierre limpio: NO disparamos onEvicted. Igual liberamos recursos.
        _ = DisposeIfNeededAsync(entry.Session);
        return true;
    }

    public async ValueTask<int> SweepExpiredAsync(CancellationToken ct = default)
    {
        var cutoff = NowTicks() - _idleTtl.Ticks;
        var evicted = 0;

        foreach (var kvp in _entries)
        {
            ct.ThrowIfCancellationRequested();
            if (kvp.Value.LastAccessTicks > cutoff) continue;

            // Re-chequeo bajo TryRemove para no barrer algo que se tocó recién.
            if (!_entries.TryRemove(kvp.Key, out var entry)) continue;
            if (entry.LastAccessTicks > cutoff)
            {
                // Se tocó entre el snapshot y el remove: re-insertar y saltear.
                _entries.TryAdd(kvp.Key, entry);
                continue;
            }

            _logger.LogDebug("Sesión barrida por inactividad: {SessionId}", kvp.Key);
            if (_onEvicted is not null)
            {
                try { await _onEvicted(kvp.Key, entry.Session, ct); }
                catch (Exception ex) { _logger.LogError(ex, "onEvicted falló para {SessionId}", kvp.Key); }
            }
            await DisposeIfNeededAsync(entry.Session);
            evicted++;
        }

        return evicted;
    }

    private long NowTicks() => _clock.GetUtcNow().UtcTicks;

    private static async ValueTask DisposeIfNeededAsync(TSession session)
    {
        switch (session)
        {
            case IAsyncDisposable ad: await ad.DisposeAsync(); break;
            case IDisposable d:       d.Dispose();            break;
        }
    }
}
