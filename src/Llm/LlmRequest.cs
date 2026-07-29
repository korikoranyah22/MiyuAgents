
namespace MiyuAgents.Llm;
public sealed record LlmRequest
{
    public required string              Model        { get; init; }
    public          string?             SystemPrompt { get; init; }
    public required IReadOnlyList<ConversationMessage> Messages { get; init; }

    // Tool calling
    public IReadOnlyList<ToolDefinition>? Tools      { get; init; }
    public string?                        ToolChoice { get; init; }  // "auto" | "none" | tool name

    // Sampling
    public float? Temperature  { get; init; }
    public int?   MaxTokens    { get; init; }

    /// <summary>
    /// Nucleus-sampling cutoff in [0,1].  When null, the gateway falls back
    /// to the provider default (usually 1.0 = no truncation).  All four of
    /// our gateways (DeepSeek, Groq, Claude, Ollama) accept this.
    /// </summary>
    public float? TopP         { get; init; }

    /// <summary>
    /// Top-K sampling cutoff (positive integer).  Only Claude and Ollama
    /// expose this knob — DeepSeek and Groq are OpenAI-compatible and
    /// silently ignore it.  Their gateways drop the field on the wire.
    /// </summary>
    public int?   TopK         { get; init; }

    // Vision: base64-encoded images, parallel to the last user message
    public IReadOnlyList<string>? Images    { get; init; }
    public IReadOnlyList<string>? ImageMediaTypes { get; init; }

    /// <summary>
    /// Toggles thinking / extended-reasoning mode on providers that support it
    /// (e.g. DeepSeek V4 maps this to <c>thinking: { type: "enabled"/"disabled" }</c>;
    /// Anthropic / OpenAI thinking variants have analogous knobs).
    /// <list type="bullet">
    ///   <item><c>null</c> (default) → let the provider decide.  DeepSeek V4 defaults
    ///         to ENABLED, which silently ignores temperature/top_p/penalties.</item>
    ///   <item><c>true</c> → force-enable.  The response will include reasoning content.</item>
    ///   <item><c>false</c> → force-disable.  Temperature works normally; no
    ///         reasoning_content is emitted.  Use this for pipeline-internal LLM calls
    ///         (summary, fact extraction, etc.) where reasoning trace is wasted output
    ///         and sampling control matters.</item>
    /// </list>
    /// Gateways that don't recognise the flag silently ignore it.
    /// </summary>
    public bool? EnableThinking { get; init; }

    /// <summary>
    /// Local observability dimensions for this call. Gateways may log or meter
    /// them, but must never serialize them into the provider request.
    /// </summary>
    public LlmCallMetadata? Metadata { get; init; }
}

/// <summary>
/// Provider-neutral call attribution. Values are operational labels, never
/// prompt content. RunId is trace-only and must not be used as a metric label.
/// </summary>
public sealed record LlmCallMetadata(
    string Workflow,
    string Node,
    string Phase,
    string? RunId = null,
    PromptCacheDiagnostics? Cache = null);

/// <summary>
/// Content-free diagnostics for exact-prefix cache experiments. Hashes are
/// one-way fingerprints and must never contain prompt text.
/// </summary>
public sealed record PromptCacheDiagnostics(
    string LayoutVersion,
    string Variant,
    string StablePrefixHash,
    int StablePrefixChars,
    int RegistryEntityCount = 0);
