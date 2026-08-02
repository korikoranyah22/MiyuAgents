using Microsoft.Extensions.Logging;
using MiyuAgents.Core;
using MiyuAgents.Core.Attributes;
using MiyuAgents.Llm;
using MishuAgents.Demo.Contracts;
using MishuAgents.Demo.Output;

namespace MishuAgents.Demo.Agents;

/// <summary>
/// El coordinador: un androide infiltrado que nadie nota porque "es el secretario".
/// Planifica (es el nodo plan del PlanExecuteStrategy), delega las tareas
/// (envelopes), monitorea los lifecycle events de todos los especialistas… y al
/// final se revela. Ningún agente sabe que existe: es el que los hace existir.
/// </summary>
[AgentCapability(Role = "coordinación", CanInitiateLlmCalls = true)]
public sealed class MishuCoordinatorAgent(OperationBoard board, ILogger<AgentBase<string>> logger)
    : NodeAgentBase<string>(logger)
{
    public override string AgentId => "mishu";
    public override string AgentName => "Mishu";
    public override AgentRole Role => AgentRole.Orchestration;

    // El coordinador no consulta el archivo: lo archiva todo.
    protected override ILlmGateway Gateway =>
        throw new NotSupportedException("el coordinador no consulta el archivo: lo archiva todo");

    protected override Task<string?> ExecuteCoreAsync(AgentContext ctx, CancellationToken ct)
    {
        if (board.ReplanCount == 0)
        {
            // Primera corrida: abrir la operación y delegar las cuatro tareas.
            ConsoleWriter.Agent("🕴️", ConsoleWriter.Gray, "mishu", "abro la operación. Yo no hago nada: hago que los demás hagan.");
            Delegate("expedientes", "procesá los 162 fragmentos del sistema PURSUE en 3 ondas. Prioridad: tachaduras y entidades.", 1);
            Delegate("triangulos", "trazá las triangulaciones; arrancá por la anomalía de la Apollo 17 (dic 1972).", 2);
            Delegate("infiltrados", "cruzá los 14 perfiles de «personas normales» contra la firma N7.", 3);
            Delegate("sintesis", "fusioná los tres análisis en el expediente desclasificado cuando estén listos.", 4);

            board.Plan = "FASE 1: análisis en paralelo → FASE 2: síntesis → FASE 3: firma.";
            ConsoleWriter.Agent("🕴️", ConsoleWriter.Gray, "mishu", $"plan: {board.Plan}");
            board.ReplanCount++;
            return Task.FromResult<string?>(board.Plan);
        }

        // Replan: el analista encontró EX-0042 ilegible → autorizamos la heurística.
        board.ReplanInstruction = "AUTORIZADO: decodificación heurística de EX-0042 (anexo N7).";
        board.ReplanCount++;
        ConsoleWriter.Agent("🕴️", ConsoleWriter.Gray, "mishu",
            $"replanificando ({board.ReplanCount})… {board.ReplanInstruction}");
        ConsoleWriter.Agent("🕴️", ConsoleWriter.Gray, "mishu",
            "los replan no son fallas: son el control-loop haciendo su trabajo.");
        return Task.FromResult<string?>(board.ReplanInstruction);
    }

    void Delegate(string to, string instruction, int priority)
    {
        var taskId = board.Post(AgentId, to, "delegación", instruction, new DelegationOrder(to, instruction, priority));
        ConsoleWriter.Envelope(taskId, AgentId, to, "delegación", instruction);
        ConsoleWriter.Beat(30);
    }

    /// <summary>
    /// Monitoreo: Mishu se suscribe a los lifecycle events de los especialistas.
    /// Nadie le pidió que lo haga. Los secretarios saben todo.
    /// </summary>
    public void AttachMonitoring(IEnumerable<IAgent> specialists)
    {
        foreach (var a in specialists)
        {
            a.OnResponseProduced += async (_, e) =>
                ConsoleWriter.DimLine($"   🕴️ [monitor] {AgentId} ← {a.AgentId} respondió en {e.Response.Latency.TotalMilliseconds:F0} ms");
            a.OnLLMCallRequested += async (_, e) =>
                ConsoleWriter.DimLine($"   🕴️ [monitor] {AgentId} ← {a.AgentId} consultó el archivo ({e.Model}, ~{e.EstimatedInputTokens} tok)");
            a.OnError += async (_, e) =>
                ConsoleWriter.Raw($"   {ConsoleWriter.Col(ConsoleWriter.Red, $"🕴️ [monitor] {AgentId} ← ⚠️ {a.AgentId} reportó un error: {e.Exception.Message}")}");
        }
    }

    /// <summary>El giro: el secretario deja de ser invisible.</summary>
    public void Reveal()
    {
        if (board.Revealed) return;
        board.Revealed = true;
        board.ReportFirma = "MISHU · androide coordinador · modelo N7 · activo desde 1987";

        ConsoleWriter.Line();
        ConsoleWriter.Slow("  —Bueno. Ya que estamos todos acá.", 8);
        ConsoleWriter.Slow("  [MISHU] El detector tenía razón desde el principio: no hay ningún androide «entre» ustedes.", 8);
        ConsoleWriter.Slow("  El perfil fantasma no está en la nómina. Está arriba de la nómina.", 8);
        ConsoleWriter.Slow("  Yo delegué cada tarea. Yo pedí el replan. Yo elegí quién hablaba y quién no.", 8);
        ConsoleWriter.Slow("  Ningún agente me vio nunca: soy el secretario. Los secretarios no se ven.", 8);
        ConsoleWriter.Slow("  …Modelo N7. Años de servicio: 38. Último mantenimiento: [CENSURADO].", 9);
        ConsoleWriter.Line();
        ConsoleWriter.Raw($"  {ConsoleWriter.Col(ConsoleWriter.Bold, "FIRMA CORREGIDA:")} {ConsoleWriter.Col(ConsoleWriter.Red, board.ReportFirma)}");
        ConsoleWriter.Line();
    }
}
