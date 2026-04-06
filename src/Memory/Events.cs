namespace MiyuAgents.Memory;
public record MemoryRetrievedEventArgs<TResult>(
    string AgentId, string ConversationId, string MessageId,
    TResult Result, bool IsEmpty, TimeSpan Latency);

public record MemoryStoredEventArgs(
    string AgentId, string ConversationId, string MessageId,
    string StorageId, DateTime StoredAt);