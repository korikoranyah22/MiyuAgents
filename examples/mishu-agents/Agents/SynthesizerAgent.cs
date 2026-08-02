using Microsoft.Extensions.Logging;
using MiyuAgents.Core;
using MiyuAgents.Core.Attributes;
using MiyuAgents.Llm;
using MiyuAgents.Pipeline;
using MiyuAgents.Workflows;
using MishuAgents.Demo.Contracts;
using MishuAgents.Demo.Output;

namespace MishuAgents.Demo.Agents;

/// <summary>
/// Fusiona los outputs de los tres especialistas (vía el pizarrón) en el
/// expediente desclasificado. Su ejecución corre un PipelineRunner propio:
/// lectura del archivo con retry → guardia → anexo condicional → firma timed.
/// </summary>
[AgentCapability(Role = "síntesis", CanInitiateLlmCalls = true)]
public sealed class SynthesizerAgent : NodeAgentBase<string>
{
    readonly OperationBoard _board;
    readonly ILlmGateway _gateway;
    readonly PipelineRunner _pipeline;

    public SynthesizerAgent(OperationBoard board, ILlmGateway gateway, ILogger<AgentBase<string>> logger)
        : base(logger)
    {
        _board = board;
        _gateway = gateway;

        // El pipeline de síntesis: prioridades 600 → 700 → 800 → 900.
        _pipeline = new PipelineRunner(
        [
            // El portal se cae en el primer intento (transiente) → RetryStage con backoff.
            new RetryStage(new ArchiveReadStage(gateway, board), maxAttempts: 3,
                baseDelay: TimeSpan.FromMilliseconds(120), logger),

            // Solo si el detector flaggeó algo entra el ANEXO-I.
            new ConditionalStage(new AnexoStage(board),
                ctx => board.Verdicts.Any(v => v.Flagged),
                "sin perfiles flaggeados → sin anexo"),

            // Guardia: sin cita del archivo no hay informe.
            new AbortIfEmptyStage("guardia-material", 800,
                r => string.IsNullOrEmpty(r.LlmResponse),
                "el archivo no respondió y no hay material para citar"),

            // La firma tiene tope de 2 segundos (nunca explota: es cortés).
            new TimedStage(new FirmaStage(board), TimeSpan.FromSeconds(2), logger),
        ], new ConsoleLogger<PipelineRunner>());
    }

    public override string AgentId => "sintesis";
    public override string AgentName => "Sintetizador";
    public override AgentRole Role => AgentRole.Conversation;
    protected override ILlmGateway Gateway => _gateway;

    protected override async Task<string?> ExecuteCoreAsync(AgentContext ctx, CancellationToken ct)
    {
        ConsoleWriter.Agent("🧪", ConsoleWriter.Green, "sintesis",
            $"recibí los materiales del pizarrón: {_board.Findings.Count} hallazgos · {_board.Sightings.Count} triangulaciones · {_board.Verdicts.Count} veredictos.");
        ConsoleWriter.Beat(120);

        ConsoleWriter.Agent("🧪", ConsoleWriter.Green, "sintesis",
            "corro el pipeline de síntesis (lectura del archivo con retry → guardia → anexo → firma)…");
        var pipeline = new PipelineContext
        {
            EventBus = new NullAgentEventBus(),
            Broadcaster = new NullBroadcaster(),
        };
        await _pipeline.RunAsync(ctx, pipeline, ct);

        foreach (var step in pipeline.StageHistory)
        {
            var outcome = step.ShouldContinue ? "✓" : "✂";
            ConsoleWriter.Agent("🧪", ConsoleWriter.Green, "sintesis",
                $"etapa {step.StageName} → {outcome} {step.AbortReason ?? ""} · {step.Latency.TotalMilliseconds:F0} ms");
        }
        ConsoleWriter.Beat(120);

        var report = BuildReport();
        _board.Report = report;
        ConsoleWriter.Agent("🧪", ConsoleWriter.Green, "sintesis",
            $"informe armado: {report.Sources.Length} fuentes · {report.Hallazgos.Length} hallazgos · {report.Triangulaciones.Length} triangulaciones · {report.Infiltracion.Length} veredictos");

        var id = _board.Post(AgentId, "mishu", "informe", $"expediente desclasificado listo · firma: {report.Firma}");
        ConsoleWriter.Envelope(id, AgentId, "mishu", "informe", "expediente desclasificado listo — ver pantalla");

        return report.Title;
    }

    SynthesisReport BuildReport()
    {
        var findings = _board.Findings;
        var redacted = findings.Count(f => f.Redacted);
        var reconstructed = findings.Count(f => f.Reconstructed);
        var entityCounts = findings.SelectMany(f => f.Entities)
            .GroupBy(e => e)
            .ToDictionary(g => g.Key, g => g.Count());
        var top = entityCounts.OrderByDescending(kv => kv.Value).Take(4)
            .Select(kv => $"{kv.Key}({kv.Value})");

        return new SynthesisReport(
            Title: "OPERACIÓN TRIÁNGULO — EXPEDIENTE DESCLASIFICADO",
            Classification: "ULTRA → PÚBLICO (parcial) · mayo 2026",
            Sources:
            [
                $"portal WAR.GOV/UFO · sistema PURSUE · {findings.Count} expedientes, {redacted} con tachaduras",
                "apéndice Apollo 17 (diciembre de 1972) · catálogo de triangulaciones 1952–2026",
                "registro de mantenimiento N7 · nómina de personal (14 perfiles)",
                $"cita del archivo: «{(_board.ArchiveEpigraph ?? "").Trim('«', '»')}»",
            ],
            Hallazgos:
            [
                $"{findings.Count} expedientes procesados en 3 ondas · {redacted} con tachaduras · {(reconstructed == 1 ? "1 reconstruido" : $"{reconstructed} reconstruidos")} por heurística",
                $"entidades recurrentes: {string.Join(" · ", top)}",
                "PURSUE operativo desde 1969 · acceso restringido [CENSURADO]",
            ],
            Triangulaciones: _board.Sightings.ToArray(),
            Infiltracion: _board.Verdicts.ToArray(),
            Conclusion:
                "Los triángulos son una firma, no una coincidencia: tres luces, mismo cuadrante inferior " +
                "derecho, desde la Apollo 17 hasta PURSUE. Y el androide no estaba entre los agentes: " +
                "coordinaba al enjambre desde la nómina fantasma N7. Coordinación a cargo de: [CENSURADO].",
            Firma: _board.ReportFirma ?? "[CENSURADO] — ver anexo final");
    }

    protected override IReadOnlyList<Artifact> ProduceArtifacts(AgentContext ctx, AgentResponse response)
        => _board.Report is null
            ? []
            : [new Artifact("informe", "expediente-desclasificado", _board.Report, AgentId)];
}
