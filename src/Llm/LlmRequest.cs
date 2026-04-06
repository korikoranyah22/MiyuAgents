
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

    // Vision: base64-encoded images, parallel to the last user message
    public IReadOnlyList<string>? Images    { get; init; }
    public IReadOnlyList<string>? ImageMediaTypes { get; init; }
}