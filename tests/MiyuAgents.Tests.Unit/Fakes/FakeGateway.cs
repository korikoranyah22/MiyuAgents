using System.Runtime.CompilerServices;
using MiyuAgents.Llm;

namespace MiyuAgents.Tests.Unit.Fakes;

/// <summary>
/// Configurable ILlmGateway stub for unit testing.
/// Returns preset responses without any HTTP calls.
/// </summary>
public sealed class FakeGateway : ILlmGateway
{
    private readonly LlmGatewayStats _stats = new();
    private readonly Func<LlmRequest, LlmResponse>? _completeFactory;

    public string                ProviderName    { get; }
    public IReadOnlyList<string> SupportedModels { get; }

    public FakeGateway(
        string providerName             = "fake",
        string[]? supportedModels       = null,
        Func<LlmRequest, LlmResponse>? completeFactory = null)
    {
        ProviderName    = providerName;
        SupportedModels = supportedModels ?? [providerName, "default"];
        _completeFactory = completeFactory;
    }

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        var usage = new LlmUsage(100, 50);
        _stats.Record(usage);

        var response = _completeFactory?.Invoke(request)
            ?? new LlmResponse("fake response", usage, "stop", []);

        return Task.FromResult(response);
    }

    public async IAsyncEnumerable<LlmChunk> StreamAsync(
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new LlmChunk("fake ", IsComplete: false, IsError: false);
        yield return new LlmChunk("chunk", IsComplete: false, IsError: false);
        var usage = new LlmUsage(100, 2);
        _stats.Record(usage);
        yield return new LlmChunk("",     IsComplete: true,  IsError: false, FinalUsage: usage);
    }

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        => Task.FromResult(new float[] { 0.1f, 0.2f, 0.3f });

    public LlmGatewayStats GetStats() => _stats;

    // ── Factory helpers ──────────────────────────────────────────────────────

    public static FakeGateway ForProvider(string name, params string[] models)
        => new(name, models.Length > 0 ? models : [name]);

    public static FakeGateway WithResponse(string content, string providerName = "fake")
        => new(providerName, completeFactory: _ =>
            new LlmResponse(content, new LlmUsage(100, 50), "stop", []));
}
