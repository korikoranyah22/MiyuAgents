using MiyuAgents.Llm;

namespace MiyuAgents.Core.Events;
public record MessageReceivedEventArgs(
    string AgentId, string ConversationId, string MessageId, string Content);

public record LlmCallRequestedEventArgs(
    string AgentId, string ConversationId, string MessageId,
    string Model, int EstimatedInputTokens, DateTime RequestedAt);

public record LlmCallRespondedEventArgs(
    string AgentId, string ConversationId, string MessageId,
    LlmUsage Usage, TimeSpan Latency, DateTime RespondedAt);

public record AgentResponseProducedEventArgs(
    string AgentId, string ConversationId, string MessageId,
    AgentResponse Response);

public record AgentErrorEventArgs(
    string AgentId, string MessageId, Exception Exception);