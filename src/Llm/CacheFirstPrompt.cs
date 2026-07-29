using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MiyuAgents.Llm;

/// <summary>
/// Provider-neutral prompt layout optimized for exact-prefix caches.
/// The framework owns the ordering; applications own the contents.
/// </summary>
public sealed record CacheFirstPrompt
{
    public required string Common { get; init; }
    public IReadOnlyList<ConversationMessage> Session { get; init; } = [];
    public IReadOnlyList<ConversationMessage> Role { get; init; } = [];
    public IReadOnlyList<ConversationMessage> Step { get; init; } = [];
    public IReadOnlyList<ConversationMessage> Delta { get; init; } = [];

    /// <summary>
    /// Produces COMMON → SESSION → ROLE → STEP → DELTA regardless of object
    /// initializer order. Empty messages are omitted without rewriting content.
    /// </summary>
    public IReadOnlyList<ConversationMessage> ToMessages() =>
        Session.Concat(Role).Concat(Step).Concat(Delta)
            .Where(message => !string.IsNullOrEmpty(message.Content) || message.ToolCalls is { Count: > 0 })
            .ToArray();

    /// <summary>
    /// Builds the common <see cref="LlmRequest"/> wire shape. Gateways remain
    /// responsible only for provider translation and never for prompt ordering.
    /// </summary>
    public LlmRequest ToRequest(
        string model,
        LlmCallMetadata? metadata = null,
        IReadOnlyList<ToolDefinition>? tools = null,
        string? toolChoice = null,
        float? temperature = null,
        int? maxTokens = null,
        float? topP = null,
        int? topK = null,
        bool? enableThinking = null) =>
        new()
        {
            Model = model,
            SystemPrompt = Common,
            Messages = ToMessages(),
            Tools = CanonicalTools(tools),
            ToolChoice = toolChoice,
            Temperature = temperature,
            MaxTokens = maxTokens,
            TopP = topP,
            TopK = topK,
            EnableThinking = enableThinking,
            Metadata = metadata,
        };

    /// <summary>
    /// Tool order is part of the cached wire. Sort by stable identity so callers
    /// backed by dictionaries or discovery order cannot churn the prefix.
    /// </summary>
    public static IReadOnlyList<ToolDefinition>? CanonicalTools(IReadOnlyList<ToolDefinition>? tools) =>
        tools is null
            ? null
            : tools.OrderBy(tool => tool.Name, StringComparer.Ordinal).ToArray();

    /// <summary>
    /// Fingerprints the reusable COMMON + SESSION prefix without retaining its
    /// content. ROLE, STEP and DELTA are intentionally excluded.
    /// </summary>
    public PromptCacheDiagnostics DiagnoseStablePrefix(
        string layoutVersion,
        string variant,
        int registryEntityCount = 0,
        IReadOnlyList<ToolDefinition>? tools = null)
    {
        var canonical = new StringBuilder(Common.Length + Session.Sum(x => x.Content.Length) + 64);
        AppendLayer(canonical, "system", Common);
        foreach (var message in Session)
            AppendLayer(canonical, message.Role, message.Content);
        foreach (var tool in CanonicalTools(tools) ?? [])
            AppendLayer(canonical, "tool", JsonSerializer.Serialize(tool));

        var text = canonical.ToString();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        return new PromptCacheDiagnostics(
            layoutVersion, variant, hash, text.Length, registryEntityCount);
    }

    private static void AppendLayer(StringBuilder target, string role, string content)
    {
        target.Append(role.Length).Append(':').Append(role)
            .Append('|').Append(content.Length).Append(':').Append(content);
    }
}
