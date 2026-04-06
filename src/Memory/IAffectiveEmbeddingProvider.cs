public interface IAffectiveEmbeddingProvider : IEmbeddingProvider
{
    /// <summary>
    /// Embed the emotional/affective content of a text separately.
    /// Returns a vector in affective space (valence/arousal dimensions).
    /// Allows weighted combination with semantic vector at query time.
    /// </summary>
    Task<float[]> EmbedAffectiveAsync(string text, CancellationToken ct = default);
}