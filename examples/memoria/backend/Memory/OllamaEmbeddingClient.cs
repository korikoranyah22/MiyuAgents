using System.Text;
using System.Text.Json;

namespace Memoria.Memory;

/// <summary>
/// Generates text embeddings via the Ollama REST API.
/// Endpoint: POST /api/embeddings  →  { "embedding": [float, ...] }
///
/// Recommended models in Ollama:
///   nomic-embed-text  (768-dim, fast)
///   mxbai-embed-large (1024-dim, higher quality)
///
/// Implements IEmbeddingProvider so it can be used with MemoryAgentBase.
/// </summary>
public sealed class OllamaEmbeddingClient(string host, int port, string model) : IEmbeddingProvider
{
    private readonly HttpClient _http = new()
    {
        BaseAddress = new Uri($"http://{host}:{port}"),
        Timeout     = TimeSpan.FromSeconds(60)
    };

    private int _dimensions;

    public string ModelName  => model;
    public int    Dimensions => _dimensions; // 0 until the first embed call

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new { model, prompt = text });

        var res = await _http.PostAsync("/api/embeddings",
            new StringContent(body, Encoding.UTF8, "application/json"), ct);

        res.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var vector = doc.RootElement
            .GetProperty("embedding")
            .EnumerateArray()
            .Select(e => e.GetSingle())
            .ToArray();

        _dimensions = vector.Length;
        return vector;
    }
}
