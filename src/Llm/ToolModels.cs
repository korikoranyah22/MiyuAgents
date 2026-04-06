namespace MiyuAgents.Llm;
public sealed record ToolDefinition(string Name, string Description, object InputSchema);
public sealed record ToolCall(string Id, string FunctionName, string ArgumentsJson);
/// <param name="Name">
/// Optional speaker name. Passed to the LLM API as the native "name" field so the
/// model can distinguish multiple speakers sharing the same role (e.g. two agents
/// both writing as "assistant"). Must be non-empty when provided.
/// </param>
public sealed record ConversationMessage(string Role, string Content, string? Name = null);