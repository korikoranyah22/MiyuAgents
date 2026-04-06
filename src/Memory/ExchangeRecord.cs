using MiyuAgents.Llm;

namespace MiyuAgents.Memory;

/// <summary>
/// The complete record of a single exchange: user input, agent output, metadata.
/// Passed to consolidation after delivery.
/// </summary>
public sealed record ExchangeRecord(
    string ConversationId,
    string MessageId,
    string UserMessage,
    string AgentResponse,
    string ProfileId,
    string CharacterId,
    string ProfileName,
    IReadOnlyList<ConversationMessage> FullHistory,
    DateTime ExchangedAt
);