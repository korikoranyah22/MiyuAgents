
namespace MiyuAgents.Llm;
public sealed record LlmChunk(
    string    Delta,
    bool      IsComplete,
    bool      IsError,
    LlmUsage? FinalUsage = null,   // only set on the last chunk
    string?   Reasoning = null,    // reasoning content from DeepSeek Reasoner models
    // Tool calls accumulated by the gateway across streaming deltas.
    // Most chunks have this null; the gateway emits ONE chunk with
    // ToolCalls populated (and IsComplete = true) when the model
    // signals finish_reason = "tool_calls" so the caller can pause
    // text streaming, execute the tools, and continue.
    IReadOnlyList<ToolCall>? ToolCalls = null
);