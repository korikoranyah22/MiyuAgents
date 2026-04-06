using System.Text;
using System.Text.Json;

namespace Memoria.Memory;

/// <summary>
/// Qdrant REST client that implements IMemoryStore&lt;FactQuery, List&lt;FactHit&gt;&gt;.
/// Uses the Qdrant HTTP API directly — no extra NuGet dependency needed.
///
/// The collection name is fixed at construction time so the store is fully
/// self-contained and compatible with MemoryAgentBase.
/// </summary>
public sealed class QdrantMemoryClient(string host, int port, string collection)
    : IMemoryStore<FactQuery, List<FactHit>>
{
    private readonly HttpClient _http = new()
    {
        BaseAddress = new Uri($"http://{host}:{port}"),
        Timeout     = TimeSpan.FromSeconds(30)
    };

    // ── IMemoryStore implementation ───────────────────────────────────────────

    /// <summary>No-op: collection is created lazily on the first StoreAsync call.</summary>
    public Task EnsureReadyAsync(CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>Search by pre-embedded vector. Filters by MinScore.</summary>
    public async Task<List<FactHit>> SearchAsync(FactQuery query, CancellationToken ct = default)
    {
        if (!await CollectionExistsAsync(ct)) return [];

        var body = JsonSerializer.Serialize(new
        {
            vector       = query.Vector,
            limit        = query.Limit,
            with_payload = true
        });

        var res = await _http.PostAsync(
            $"/collections/{collection}/points/search",
            new StringContent(body, Encoding.UTF8, "application/json"), ct);

        if (!res.IsSuccessStatusCode) return [];

        using var doc  = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var hits = new List<FactHit>();

        foreach (var hit in doc.RootElement.GetProperty("result").EnumerateArray())
        {
            var score   = hit.GetProperty("score").GetSingle();
            if (score < query.MinScore) continue;

            var payload = hit.GetProperty("payload").EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");

            var id = hit.TryGetProperty("id", out var idProp) ? idProp.ToString() : "";
            hits.Add(new FactHit(
                id,
                payload.GetValueOrDefault("label", ""),
                payload.GetValueOrDefault("value", ""),
                payload.GetValueOrDefault("text", ""),
                score));
        }

        return hits;
    }

    /// <summary>Not used in this example — returns empty list.</summary>
    public Task<List<FactHit>> GetAllAsync(string scopeId, CancellationToken ct = default)
        => Task.FromResult(new List<FactHit>());

    /// <summary>
    /// Stores a FactEntry. Creates the Qdrant collection on the first call
    /// (vector size is determined from the entry's embedding).
    /// </summary>
    public async Task<string> StoreAsync(object entry, CancellationToken ct = default)
    {
        var fact = (FactEntry)entry;
        await EnsureCollectionAsync(fact.Vector.Length, ct);

        var id = Guid.NewGuid();
        await UpsertPointAsync(id, fact.Vector, new Dictionary<string, string>
        {
            ["label"] = fact.Label,
            ["value"] = fact.Value,
            ["text"]  = fact.Text
        }, ct);

        return id.ToString();
    }

    /// <summary>Upsert with a specific ID (parses the ID as Guid).</summary>
    public async Task<string> UpsertAsync(string id, object entry, CancellationToken ct = default)
    {
        var fact = (FactEntry)entry;
        await EnsureCollectionAsync(fact.Vector.Length, ct);

        var guid = Guid.TryParse(id, out var g) ? g : Guid.NewGuid();
        await UpsertPointAsync(guid, fact.Vector, new Dictionary<string, string>
        {
            ["label"] = fact.Label,
            ["value"] = fact.Value,
            ["text"]  = fact.Text
        }, ct);

        return guid.ToString();
    }

    /// <summary>Delete a point by Guid string.</summary>
    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new { points = new[] { id } });
        await _http.PostAsync(
            $"/collections/{collection}/points/delete?wait=true",
            new StringContent(body, Encoding.UTF8, "application/json"), ct);
    }

    // ── Collection management ─────────────────────────────────────────────────

    /// <summary>Deletes the collection (ignores 404 if it does not exist yet).</summary>
    public async Task DeleteCollectionAsync(CancellationToken ct = default)
        => await _http.DeleteAsync($"/collections/{collection}", ct);

    public async Task<bool> CollectionExistsAsync(CancellationToken ct = default)
    {
        var res = await _http.GetAsync($"/collections/{collection}", ct);
        return res.IsSuccessStatusCode;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task EnsureCollectionAsync(int vectorSize, CancellationToken ct)
    {
        if (await CollectionExistsAsync(ct)) return;

        var body = JsonSerializer.Serialize(new
        {
            vectors = new { size = vectorSize, distance = "Cosine" }
        });
        await _http.PutAsync($"/collections/{collection}",
            new StringContent(body, Encoding.UTF8, "application/json"), ct);
    }

    private async Task UpsertPointAsync(
        Guid                       id,
        float[]                    vector,
        Dictionary<string, string> payload,
        CancellationToken          ct)
    {
        var body = JsonSerializer.Serialize(new
        {
            points = new[] { new { id = id.ToString(), vector, payload } }
        });
        var res = await _http.PutAsync(
            $"/collections/{collection}/points?wait=true",
            new StringContent(body, Encoding.UTF8, "application/json"), ct);
        res.EnsureSuccessStatusCode();
    }
}
