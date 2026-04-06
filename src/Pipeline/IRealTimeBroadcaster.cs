namespace MiyuAgents.Pipeline;

/// <summary>
/// Channel for pushing real-time updates to connected clients.
/// The consumer implements this (e.g., wrapping SignalR IHubContext).
/// </summary>
public interface IRealtimeBroadcaster
{
    Task SendChunkAsync(string conversationId, string chunk, bool isComplete, CancellationToken ct = default);
    Task SendStatusAsync(string conversationId, string status, CancellationToken ct = default);
    Task SendErrorAsync(string conversationId, string error, CancellationToken ct = default);
}