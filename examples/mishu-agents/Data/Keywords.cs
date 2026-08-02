namespace MishuAgents.Demo.Data;

/// <summary>
/// Las entidades que el Analista de Expedientes extrae de los fragmentos: un
/// vocabulario fijo del dominio (PURSUE, TRIÁNGULO, N7…). La extracción es un
/// match de subcadena — determinista y sin LLM.
/// </summary>
public static class Keywords
{
    public static readonly string[] All =
    [
        "PURSUE", "WAR.GOV", "TRIÁNGULO", "APOLLO-17", "CUADRANTE",
        "ORBE", "MASIVO", "N7", "SIN-LEGAJO", "TACHADURA",
        "BOLETA-9", "FRECUENCIA", "COORDINACIÓN",
    ];

    public static string[] Extract(string body)
        => All.Where(k => body.Contains(k, StringComparison.OrdinalIgnoreCase)).ToArray();

    /// <summary>Lista compacta de entidades para las líneas de log (máx. 6 + "…").</summary>
    public static string Compact(IEnumerable<string> entities, int max = 6)
    {
        var list = entities.Take(max).ToArray();
        var tail = entities.Count() > max ? " …" : "";
        return string.Join(" ", list) + tail;
    }
}
