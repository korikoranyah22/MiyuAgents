
namespace MiyuAgents.Llm;
public sealed record LlmChunk(
    string    Delta,
    bool      IsComplete,
    bool      IsError,
    LlmUsage? FinalUsage = null   // only set on the last chunk
);