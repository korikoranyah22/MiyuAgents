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
            // Un gateway puede exponer el mismo modelo por config y como default
            // conocido. Además, dos providers podrían declarar un alias compartido.
            // Conservamos el primero registrado, igual que el fallback del router,
            // sin permitir que una duplicación de metadata tumbe todo el host.
            .GroupBy(x => x.Model, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Gateway,
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

    /// <summary>Busca un gateway registrado por provider, sin caer al default.</summary>
    public ILlmGateway? FindByProvider(string provider) => _all.FirstOrDefault(g =>
        string.Equals(g.ProviderName, provider, StringComparison.OrdinalIgnoreCase));

    /// <summary>Providers efectivamente registrados en este proceso.</summary>
    public IReadOnlyList<string> RegisteredProviders =>
        _all.Select(g => g.ProviderName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>
    /// Walks a prioritised list of model names and returns the first
    /// (Gateway, Model) pair where the gateway actually supports the model.
    /// Returns null if none of the preferred models can be resolved.
    /// </summary>
    public (ILlmGateway Gateway, string Model)? ResolvePreferred(
        IReadOnlyList<string> preferredModels)
    {
        if (preferredModels is null or { Count: 0 })
            return null;

        foreach (var model in preferredModels)
        {
            if (_byModel.TryGetValue(model, out var gateway))
                return (gateway, model);

            // Partial match: "deepseek" → any gateway whose provider name matches
            var partial = _all.FirstOrDefault(g =>
                model.Contains(g.ProviderName, StringComparison.OrdinalIgnoreCase));

            if (partial is not null && partial.SupportedModels.Contains(model, StringComparer.OrdinalIgnoreCase))
                return (partial, model);
        }

        return null;
    }

    /// <summary>Aggregated stats snapshot across all registered gateways.</summary>
    public IReadOnlyDictionary<string, LlmGatewayStatsSnapshot> AggregatedStats() =>
        _all.ToDictionary(
            g => g.ProviderName,
            g => g.GetStats().Snapshot() with { ProviderName = g.ProviderName });
}
