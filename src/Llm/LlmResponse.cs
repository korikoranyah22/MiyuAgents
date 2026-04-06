
namespace MiyuAgents.Llm;
public sealed record LlmResponse(
    string              Content,
    LlmUsage            Usage,
    string?             FinishReason,
    IReadOnlyList<ToolCall>? ToolCalls = null
);