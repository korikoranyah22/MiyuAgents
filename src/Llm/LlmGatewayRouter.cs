namespace MiyuAgents.Llm;

/// <summary>
/// Resolves the correct ILlmGateway for a given model name.
/// Built from all registered ILlmGateway implementations at startup.
///
/// Also aggregates stats from all gateways for cross-provider reporting.
/// </summary>
public sealed class LlmGatewayRouter
{
    private readonly IReadOnlyDictionary<string, ILlmGateway> _byModel;
    private readonly IReadOnlyList<ILlmGateway>               _all;
    private readonly ILlmGateway                              _default;

    public LlmGatewayRouter(IEnumerable<ILlmGateway> gateways)
    {
        _all = gateways.ToList();

        _byModel = _all
            .SelectMany(g => g.SupportedModels.Select(m => (Model: m, Gateway: g)))
            .ToDictionary(x => x.Model, x => x.Gateway,
                          StringComparer.OrdinalIgnoreCase);

        // First registered gateway is the default fallback
        _default = _all.FirstOrDefault()
            ?? throw new InvalidOperationException(
                   "No ILlmGateway implementations registered. " +
                   "Call services.AddSingleton<ILlmGateway, YourGateway>().");
    }

    /// <summary>
    /// Resolves the gateway for the given model name.
    /// Falls back to the default gateway if not found (logs a warning).
    /// </summary>
    public ILlmGateway Resolve(string model)
    {
        if (_byModel.TryGetValue(model, out var gateway))
            return gateway;

        // Partial match: "deepseek" resolves any gateway whose provider name contains it
        var partial = _all.FirstOrDefault(g =>
            model.Contains(g.ProviderName, StringComparison.OrdinalIgnoreCase));

        return partial ?? _default;
    }

    /// <summary>Aggregated stats snapshot across all registered gateways.</summary>
    public IReadOnlyDictionary<string, LlmGatewayStatsSnapshot> AggregatedStats() =>
        _all.ToDictionary(
            g => g.ProviderName,
            g => g.GetStats().Snapshot() with { ProviderName = g.ProviderName });
}