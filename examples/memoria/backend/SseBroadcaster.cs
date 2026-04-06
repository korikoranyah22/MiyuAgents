using System.Text.Json;
using MiyuAgents.Pipeline;

namespace Memoria;

/// <summary>
/// IRealtimeBroadcaster that writes Server-Sent Events directly to an
/// ASP.NET Core HttpResponse. One instance is created per HTTP request.
/// </summary>
public sealed class SseBroadcaster(HttpResponse response) : IRealtimeBroadcaster
{
    public async Task SendChunkAsync(
        string conversationId, string chunk, bool isComplete, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(chunk)) return;
        await response.WriteAsync($"data: {chunk}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    public async Task SendStatusAsync(string conversationId, string status, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(new { status });
        await response.WriteAsync($"data: {json}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    public async Task SendErrorAsync(string conversationId, string error, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(new { error });
        await response.WriteAsync($"data: {json}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }
}
