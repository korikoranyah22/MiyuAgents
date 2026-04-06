namespace MiyuAgents.Memory;

/// <summary>
/// Tracks active memory entries across turns with decay logic.
///
/// Neurological model: each retrieval "reconsolidates" the memory (resets counter).
/// Each turn without retrieval decrements TurnsRemaining. At zero the entry is evicted.
///
/// Reference: Nader & Schafe (2000) — memory reconsolidation after recall.
/// </summary>
public sealed class MemoryWindow<TEntry>
{
    private readonly Dictionary<string, WindowEntry<TEntry>> _entries = new();
    private readonly int _defaultTurns;

    public MemoryWindow(int defaultTurns = 3)
    {
        _defaultTurns = defaultTurns;
    }

    /// <summary>
    /// Apply one turn of decay. Call at the start of each turn.
    /// Returns number of expired entries.
    /// </summary>
    public int ApplyDecay()
    {
        var expired = 0;
        foreach (var key in _entries.Keys.ToList())
        {
            _entries[key].TurnsRemaining--;
            if (_entries[key].TurnsRemaining <= 0)
            {
                _entries.Remove(key);
                expired++;
            }
        }
        return expired;
    }

    /// <summary>
    /// Update window with newly retrieved entries.
    /// Returns (newEntries, refreshedEntries).
    /// </summary>
    public (int New, int Refreshed) UpdateWith(
        IEnumerable<(string Id, TEntry Entry)> retrieved)
    {
        int added = 0, refreshed = 0;
        foreach (var (id, entry) in retrieved)
        {
            if (_entries.TryGetValue(id, out var existing))
            {
                existing.TurnsRemaining = _defaultTurns;   // LTP reset
                refreshed++;
            }
            else
            {
                _entries[id] = new WindowEntry<TEntry>(entry, _defaultTurns);
                added++;
            }
        }
        return (added, refreshed);
    }

    public IReadOnlyList<TEntry> ActiveEntries =>
        [.. _entries.Values.Select(e => e.Entry)];

    public bool Contains(string id) => _entries.ContainsKey(id);
}

internal sealed class WindowEntry<TEntry>(TEntry entry, int turns)
{
    public TEntry Entry         { get; } = entry;
    public int TurnsRemaining  { get; set; } = turns;
}