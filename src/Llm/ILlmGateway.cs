namespace MiyuAgents.Llm;

/// <summary>
/// Abstraction over any LLM provider.
/// One implementation per provider (DeepSeek, Anthropic, Ollama, OpenAI...).
/// The consumer registers one or more gateways; LlmGatewayRouter selects by model name.
/// </summary>
public interface ILlmGateway
{
    /// <summary>Provider identifier, e.g. "deepseek", "anthropic", "ollama".</summary>
    string ProviderName { get; }

    /// <summary>Model names this gateway can serve.</summary>
    IReadOnlyList<string> SupportedModels { get; }

    // ── Non-streaming ────────────────────────────────────────────────────────

    /// <summary>
    /// Single-turn completion. Used for analysis, summarization, orchestration.
    /// </summary>
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default);

    // ── Streaming ────────────────────────────────────────────────────────────

    /// <summary>
    /// Streaming completion. Used for the main conversation turn.
    /// Each yielded LlmChunk contains a delta; the final chunk contains full usage stats.
    /// </summary>
    IAsyncEnumerable<LlmChunk> StreamAsync(LlmRequest request, CancellationToken ct = default);

    // ── Embeddings ───────────────────────────────────────────────────────────

    /// <summary>
    /// Embed text to a float vector.
    /// Throws NotSupportedException if the provider does not support embeddings.
    /// </summary>
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);

    // ── Observability ────────────────────────────────────────────────────────

    /// <summary>Accumulated usage statistics for this gateway instance.</summary>
    LlmGatewayStats GetStats();
}