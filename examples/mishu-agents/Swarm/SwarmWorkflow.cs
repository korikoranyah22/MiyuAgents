using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MiyuAgents.Core;
using MiyuAgents.Workflows;
using MishuAgents.Demo.Agents;

namespace MishuAgents.Demo.Swarm;

/// <summary>
/// El árbol de la operación: un WorkflowNode raíz con PlanExecuteStrategy
/// (planifica Mishu → ejecuta el enjambre; si un especialista emite
/// NeedsReplanning, vuelve a planificar — loop-back acotado por maxReplans).
/// El nodo de ejecución es OTRO WorkflowNode con SequenceStrategy
/// (análisis → síntesis), y el de análisis es OTRO MÁS con ParallelStrategy
/// (los tres especialistas en paralelo). Tres niveles de recursión.
/// </summary>
public static class SwarmWorkflow
{
    public static WorkflowNode Build(IServiceProvider sp)
    {
        var mishu = sp.GetRequiredService<MishuCoordinatorAgent>();
        var analyst = sp.GetRequiredService<ExpedienteAnalystAgent>();
        var tracer = sp.GetRequiredService<TriangleTracerAgent>();
        var detector = sp.GetRequiredService<InfiltratorDetectorAgent>();
        var synth = sp.GetRequiredService<SynthesizerAgent>();

        var nodeLogger = sp.GetRequiredService<ILogger<AgentBase<NodeResult>>>();

        var analisis = new WorkflowNode(
            "analisis",
            new ParallelStrategy(["expedientes", "triangulos", "infiltrados"]),
            new Dictionary<string, IAgent>
            {
                ["expedientes"] = analyst,
                ["triangulos"] = tracer,
                ["infiltrados"] = detector,
            },
            nodeLogger);

        var swarm = new WorkflowNode(
            "swarm",
            new SequenceStrategy(["analisis", "sintesis"]),
            new Dictionary<string, IAgent>
            {
                ["analisis"] = analisis,
                ["sintesis"] = synth,
            },
            nodeLogger);

        return new WorkflowNode(
            "operacion",
            new PlanExecuteStrategy(planId: "mishu", execId: "swarm", maxReplans: 2),
            new Dictionary<string, IAgent>
            {
                ["mishu"] = mishu,
                ["swarm"] = swarm,
            },
            nodeLogger);
    }
}
