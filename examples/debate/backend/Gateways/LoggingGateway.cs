using System.Runtime.CompilerServices;
using System.Text.Json;
using MiyuAgents.Llm;

namespace Example.Gateways;

/// <summary>
/// Decorator that logs every LlmRequest as pretty-printed JSON to stdout
/// before delegating to the wrapped gateway.
/// Wrap any ILlmGateway with this at construction time in Program.cs.
/// </summary>
public sealed class LoggingGateway(ILlmGateway inner) : ILlmGateway
{
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public string                ProviderName    => inner.ProviderName;
    public IReadOnlyList<string> SupportedModels => inner.SupportedModels;
    public LlmGatewayStats       GetStats()       => inner.GetStats();

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        LogRequest(request);
        return await inner.CompleteAsync(request, ct);
    }

    public async IAsyncEnumerable<LlmChunk> StreamAsync(
        LlmRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        LogRequest(request);
        await foreach (var chunk in inner.StreamAsync(request, ct))
            yield return chunk;
    }

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) =>
        inner.EmbedAsync(text, ct);

    private static void LogRequest(LlmRequest req)
    {
        var json = JsonSerializer.Serialize(req, _json);
        Console.WriteLine($"\n── LlmRequest ────────────────────────────────────────\n{json}\n──────────────────────────────────────────────────────\n");
    }
}
