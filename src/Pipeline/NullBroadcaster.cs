namespace MiyuAgents.Pipeline;

/// <summary>
/// No-op real-time broadcaster. Discards all streaming output.
/// Use in tests or batch processing where real-time streaming is not needed.
/// </summary>
public sealed class NullBroadcaster : IRealtimeBroadcaster
{
    public static readonly NullBroadcaster Instance = new();
    public Task SendChunkAsync(string conversationId, string chunk, bool isComplete, CancellationToken ct = default) => Task.CompletedTask;
    public Task SendStatusAsync(string conversationId, string status, CancellationToken ct = default) => Task.CompletedTask;
    public Task SendErrorAsync(string conversationId, string error, CancellationToken ct = default) => Task.CompletedTask;
}
