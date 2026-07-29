namespace MiyuAgents.Workflows;

/// <summary>
/// Strategy "planificar → ejecutar" con LOOP-BACK (§7 del spike, "programación recursiva a nivel
/// workflows"): corre el nodo de PLANNING, después el de EJECUCIÓN; si el ejecutor emite
/// <see cref="NodeSignal.NeedsReplanning"/> (no puede seguir), vuelve al planning — acotado por
/// <paramref name="maxReplans"/>. Implementa <see cref="ISignalReactiveStrategy"/> para interceptar
/// el signal antes de que suba. planId/execId suelen ser, a su vez, WorkflowNodes (sub-workflows).
/// </summary>
public sealed class PlanExecuteStrategy(string planId, string execId, int maxReplans = 2)
    : IControlStrategy, ISignalReactiveStrategy
{
    public string Name => "plan-execute";

    public Task<ControlDecision> NextAsync(NodeState state, CancellationToken ct = default)
    {
        var last = state.History.Count > 0 ? state.History[^1] : null;
        if (last is null) return Task.FromResult(ControlDecision.Run(planId));                 // 1º planear
        if (last.Response.AgentId == planId) return Task.FromResult(ControlDecision.Run(execId)); // recién planeó → ejecutar
        return Task.FromResult(ControlDecision.Stop());                                        // ejecutó y no pidió replan → listo
    }

    public Task<ControlDecision?> OnChildSignalAsync(
        NodeState state, string childId, NodeResult result, CancellationToken ct = default)
    {
        // El ejecutor no puede seguir → volver a planificar (loop-back), mientras no pasemos el tope.
        if (result.Signal == NodeSignal.NeedsReplanning && childId == execId)
        {
            var replans = state.History.Count(h => h.Response.AgentId == planId);
            if (replans <= maxReplans)
                return Task.FromResult<ControlDecision?>(ControlDecision.Run(planId));
        }
        return Task.FromResult<ControlDecision?>(null);   // default: sube el signal
    }
}
