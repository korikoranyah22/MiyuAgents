using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace MiyuAgents.Llm;

/// <summary>
/// Base class for ILlmGateway implementations.
/// Handles: stats recording, error logging, timeout normalization.
/// Concrete gateways implement: BuildCompletePayload, BuildStreamPayload, ParseCompleteResponse, ParseStreamChunk.
/// </summary>
public abstract class GatewayBase(ILogger<GatewayBase> logger) : ILlmGateway
{
    protected abstract HttpClient Http {get ; }
    private   readonly LlmGatewayStats _stats = new();

    public abstract string                ProviderName     { get; }
    public abstract IReadOnlyList<string> SupportedModels  { get; }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await CompleteInternalAsync(request, ct);
            _stats.Record(response.Usage);
            logger.LogDebug("[{Provider}] complete {Model} — {In}in/{Out}out tokens, {Ms}ms",
                ProviderName, request.Model,
                response.Usage.InputTokens, response.Usage.OutputTokens,
                sw.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            _stats.RecordError();
            logger.LogError(ex, "[{Provider}] complete failed — model={Model}", ProviderName, request.Model);
            throw;
        }
    }

    public async IAsyncEnumerable<LlmChunk> StreamAsync(
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var chunk in StreamInternalAsync(request, ct))
        {
            if (chunk.FinalUsage is { } usage) _stats.Record(usage);
            else if (!chunk.IsError) _stats.RecordChunk();
            yield return chunk;
        }
    }

    // Implementations provide HTTP-level details
    protected abstract Task<LlmResponse>              CompleteInternalAsync(LlmRequest req, CancellationToken ct);
    protected abstract IAsyncEnumerable<LlmChunk>     StreamInternalAsync(LlmRequest req, CancellationToken ct);

    public virtual Task<float[]> EmbedAsync(string text, CancellationToken ct) =>
        throw new NotSupportedException($"{ProviderName} does not support embeddings.");

    public LlmGatewayStats GetStats() => _stats;
}