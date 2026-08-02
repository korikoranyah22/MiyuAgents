using MiyuAgents.Core;
using MiyuAgents.Llm;
using MiyuAgents.Pipeline;
using MishuAgents.Demo.Contracts;

namespace MishuAgents.Demo.Agents;

/// <summary>
/// Etapa 600: lee la cita del portal WAR.GOV/UFO vía el gateway pursue-archive.
/// El PRIMER intento falla siempre (el portal se cae a veces) → el RetryStage que
/// lo envuelve reintenta con backoff. Transiente y determinista, a propósito:
/// así el demo muestra el retry en vivo.
/// </summary>
public sealed class ArchiveReadStage(ILlmGateway gateway, OperationBoard board) : IPipelineStage
{
    int _calls;

    public string StageName => "lectura-del-archivo";
    public int Priority => 600;

    public async Task<PipelineStageResult> ExecuteAsync(AgentContext ctx, PipelineContext pipeline, CancellationToken ct)
    {
        if (Interlocked.Increment(ref _calls) == 1)
            throw new HttpRequestException("portal WAR.GOV/UFO no responde (timeout)");

        var req = new LlmRequest
        {
            Model = "pursue-archive",
            Messages = [new("user", "contexto general: OPERACIÓN TRIÁNGULO · mayo 2026")],
        };
        var resp = await gateway.CompleteAsync(req, ct);
        ctx.Results.LlmResponse = resp.Content;
        board.ArchiveEpigraph = resp.Content;
        return PipelineStageResult.Continue(StageName, note: "cita obtenida del portal");
    }
}

/// <summary>Etapa 700 (condicional): si el detector flaggeó algo, agrega el ANEXO-I.</summary>
public sealed class AnexoStage(OperationBoard board) : IPipelineStage
{
    public string StageName => "anexo-infiltración";
    public int Priority => 700;

    public Task<PipelineStageResult> ExecuteAsync(AgentContext ctx, PipelineContext pipeline, CancellationToken ct)
    {
        board.ReportAnexo = "ANEXO-I · INFILTRACIÓN: perfil fantasma PHANTOM-0 (coordinador de operaciones, sin legajo) — score 0,97. Ver sección INFILTRACIÓN.";
        return Task.FromResult(PipelineStageResult.Continue(StageName, note: "flag detectado → anexo agregado"));
    }
}

/// <summary>Etapa 900 (timed): reserva la firma. El nombre real se revela al final.</summary>
public sealed class FirmaStage(OperationBoard board) : IPipelineStage
{
    public string StageName => "firma";
    public int Priority => 900;

    public async Task<PipelineStageResult> ExecuteAsync(AgentContext ctx, PipelineContext pipeline, CancellationToken ct)
    {
        await Task.Delay(150, ct); // la firma tarda lo que tarda
        board.ReportFirma = "[CENSURADO] — ver anexo final";
        return PipelineStageResult.Continue(StageName, note: "firma reservada");
    }
}
