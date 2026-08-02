using Microsoft.Extensions.Logging;
using MiyuAgents.Core;
using MiyuAgents.Core.Attributes;
using MiyuAgents.Llm;
using MiyuAgents.Workflows;
using MishuAgents.Demo.Contracts;
using MishuAgents.Demo.Data;
using MishuAgents.Demo.Output;

namespace MishuAgents.Demo.Agents;

/// <summary>
/// Traza las triangulaciones: recorre el catálogo (la anomalía de la Apollo 17 a
/// la cabeza), cruza con los hallazgos del analista y concluye que el triángulo
/// de tres luces en el cuadrante inferior derecho es una firma, no una
/// coincidencia.
/// </summary>
[AgentCapability(Role = "trazado de triangulaciones", CanInitiateLlmCalls = true)]
public sealed class TriangleTracerAgent : NodeAgentBase<string>
{
    readonly OperationBoard _board;
    readonly ILlmGateway _gateway;

    public TriangleTracerAgent(OperationBoard board, ILlmGateway gateway, ILogger<AgentBase<string>> logger)
        : base(logger)
    {
        _board = board;
        _gateway = gateway;
    }

    public override string AgentId => "triangulos";
    public override string AgentName => "Trazador de Triangulaciones";
    public override AgentRole Role => AgentRole.Analysis;
    protected override ILlmGateway Gateway => _gateway;

    protected override async Task<string?> ExecuteCoreAsync(AgentContext ctx, CancellationToken ct)
    {
        _board.Sightings.Clear(); // idempotente ante replan

        var quote = await ConsultArchiveAsync(ctx, "Apollo 17 · diciembre 1972 · tres puntos de luz · triángulo", ct);
        ConsoleWriter.Agent("📐", ConsoleWriter.Dim, "triangulos", $"archivo: «{ConsoleWriter.Snippet(quote)}»");
        ConsoleWriter.Beat();

        foreach (var s in TriangleCatalog.Catalog)
        {
            ConsoleWriter.Agent("📐", ConsoleWriter.Yellow, "triangulos",
                $"{s.IncidentId} · {s.When} · {s.Where} · {s.Lights} luces · {s.Quadrant} · «{s.Verdict}» · confianza {s.Confidence:F2}");
            _board.Sightings.Add(s);
            ConsoleWriter.Beat(50);
        }

        var triFindings = _board.Findings.Count(f => f.Entities.Contains("TRIÁNGULO"));
        if (triFindings > 0)
        {
            ConsoleWriter.Agent("📐", ConsoleWriter.Yellow, "triangulos",
                $"crucé con los hallazgos del analista: {triFindings} fragmentos mencionan la formación triangular. La firma se repite: mismo cuadrante, tres luces, mismo objeto.");
        }
        else
        {
            ConsoleWriter.Agent("📐", ConsoleWriter.Yellow, "triangulos",
                "el analista todavía no entregó hallazgos (corremos en paralelo) — el patrón lo confirma el catálogo: mismo cuadrante, tres luces, mismo objeto.");
        }

        ConsoleWriter.Agent("📐", ConsoleWriter.Yellow, "triangulos",
            "los triángulos no son coincidencia: son firma. La Apollo 17 los vio en 1972; PURSUE los sigue viendo hoy.");
        ConsoleWriter.Beat(80);

        var summary = $"{TriangleCatalog.Catalog.Length} triangulaciones trazadas · la anomalía de la Apollo 17 (dic 1972) encabeza el patrón";
        var id = _board.Post(AgentId, "sintesis", "triangulación", summary);
        ConsoleWriter.Envelope(id, AgentId, "sintesis", "triangulación", summary);

        return summary;
    }

    protected override IReadOnlyList<Artifact> ProduceArtifacts(AgentContext ctx, AgentResponse response)
        => [new Artifact("triangulaciones", $"triangulaciones-{_board.Sightings.Count}", _board.Sightings.ToArray(), AgentId)];
}
