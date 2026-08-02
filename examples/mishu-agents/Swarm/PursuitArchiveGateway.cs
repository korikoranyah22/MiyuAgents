using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using MiyuAgents.Llm;

namespace MishuAgents.Demo.Swarm;

/// <summary>
/// El "LLM" del operativo: un gateway que lee el archivo desclasificado. Mismo
/// contrato que cualquier proveedor real (ILlmGateway / GatewayBase) — si mañana
/// hay API key, se cambia por DeepSeek/Anthropic sin tocar un agente.
/// Respuestas deterministas por tema + uso de tokens proporcional al prompt.
/// </summary>
public sealed class PursuitArchiveGateway : GatewayBase
{
    static readonly Microsoft.Extensions.Logging.ILogger<GatewayBase> Logger =
        NullLogger<GatewayBase>.Instance;

    static readonly HttpClient _http = new();

    public PursuitArchiveGateway() : base(Logger) { }

    protected override HttpClient Http => _http;
    public override string ProviderName => "pursue-archive";
    public override IReadOnlyList<string> SupportedModels => ["pursue-archive", "archivo", "default"];

    protected override Task<LlmResponse> CompleteInternalAsync(LlmRequest req, CancellationToken ct)
    {
        var prompt = string.Join(" ", req.Messages.Select(m => m.Content));
        var p = prompt.ToLowerInvariant();

        var (content, outTokens) = p switch
        {
            _ when p.Contains("apollo") || p.Contains("1972") || p.Contains("triángulo") || p.Contains("triangulo") => (
                "«…tres puntos de luz en el cuadrante inferior derecho del cielo lunar, descritos por la tripulación como un objeto físico masivo. Diciembre de 1972.» — transcripción Apollo 17, portal WAR.GOV/UFO.",
                26),
            _ when p.Contains("pursue") => (
                "«PURSUE: sistema de seguimiento de firmas anómalas. Operativo desde 1969. Nivel de acceso: [CENSURADO]. Cada consulta la autoriza el coordinador de operaciones.»",
                20),
            _ when p.Contains("n7") || p.Contains("mantenimiento") => (
                "«Registro N7: un perfil sin legajo con mantenimiento programado cada 96 horas. Coordina operaciones desde 1987. No figura fecha de alta.»",
                19),
            _ => ("«El archivo responde: [CENSURADO]. Refiná la consulta.»", 8),
        };

        var usage = new LlmUsage(InputTokens: Math.Max(8, prompt.Length / 4), OutputTokens: outTokens);
        return Task.FromResult(new LlmResponse(content, usage, "stop"));
    }

    protected override IAsyncEnumerable<LlmChunk> StreamInternalAsync(LlmRequest req, CancellationToken ct)
        => throw new NotSupportedException($"{ProviderName} no soporta streaming.");
}
