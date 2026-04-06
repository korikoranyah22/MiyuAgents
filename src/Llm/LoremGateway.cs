using System.Runtime.CompilerServices;

namespace MiyuAgents.Llm;

/// <summary>
/// A no-API fallback gateway that returns lorem ipsum text.
/// Use when no API key is configured, in tests, or for UI development.
/// Never throws — always returns a successful response.
/// </summary>
public sealed class LoremGateway : ILlmGateway
{
    private static readonly string[] _responses =
    [
        "Lorem ipsum dolor sit amet, consectetur adipiscing elit.",
        "Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
        "Ut enim ad minim veniam, quis nostrud exercitation ullamco.",
        "Duis aute irure dolor in reprehenderit in voluptate velit esse.",
        "Excepteur sint occaecat cupidatat non proident, sunt in culpa.",
        "Praesent commodo cursus magna, vel scelerisque nisl consectetur.",
        "Cras mattis consectetur purus sit amet fermentum.",
        "Nullam id dolor id nibh ultricies vehicula ut id elit.",
    ];

    private readonly LlmGatewayStats _stats = new();
    private int _counter;

    public string                ProviderName    => "lorem";
    public IReadOnlyList<string> SupportedModels => ["lorem", "default"];

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        var text  = _responses[Interlocked.Increment(ref _counter) % _responses.Length];
        var usage = new LlmUsage(100, 50);
        _stats.Record(usage);

        return Task.FromResult(new LlmResponse(text, usage, "stop", []));
    }

    public async IAsyncEnumerable<LlmChunk> StreamAsync(
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var text  = _responses[Interlocked.Increment(ref _counter) % _responses.Length];
        var words = text.Split(' ');

        foreach (var word in words)
        {
            if (ct.IsCancellationRequested) yield break;
            await Task.Delay(20, ct);
            yield return new LlmChunk(word + " ", IsComplete: false, IsError: false, FinalUsage: null);
        }

        var usage = new LlmUsage(100, words.Length);
        _stats.Record(usage);
        yield return new LlmChunk("", IsComplete: true, IsError: false, FinalUsage: usage);
    }

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        // Return a deterministic pseudo-random 768-dim vector based on text hash
        var hash = text.GetHashCode();
        var rng  = new Random(hash);
        var vec  = new float[768];
        for (int i = 0; i < vec.Length; i++)
            vec[i] = (float)(rng.NextDouble() * 2 - 1);
        // Normalize
        var norm = MathF.Sqrt(vec.Sum(x => x * x));
        for (int i = 0; i < vec.Length; i++) vec[i] /= norm;
        return Task.FromResult(vec);
    }

    public LlmGatewayStats GetStats() => _stats;
}
