/// <summary>
/// Abstraction over any embedding model (Ollama, OpenAI, HuggingFace...).
/// Returns float arrays. The dimension is determined by the implementation.
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>Human-readable name of the underlying model (for logging).</summary>
    string ModelName { get; }

    /// <summary>Dimension of output vectors.</summary>
    int Dimensions { get; }

    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);

    /// <summary>Batch embed for efficiency. Default implementation loops.</summary>
    async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IEnumerable<string> texts, CancellationToken ct = default)
    {
        var results = new List<float[]>();
        foreach (var text in texts)
            results.Add(await EmbedAsync(text, ct));
        return results;
    }
}