using MiyuAgents.Core;
using MiyuAgents.Llm;

namespace MiyuAgents.Pipeline;

public sealed record TurnResult(
    AgentContext               Context,
    IReadOnlyList<PipelineStageResult> StageHistory,
    TimeSpan                   TotalLatency
)
{
    public string?   Response   => Context.Results.LlmResponse;
    public LlmUsage? TokenUsage => Context.Results.TokenUsage;
    public bool      WasAborted => StageHistory.Any(s => !s.ShouldContinue);
}