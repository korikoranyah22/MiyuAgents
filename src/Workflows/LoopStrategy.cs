namespace MiyuAgents.Workflows;

/// <summary>
/// Strategy "Loop" (ReAct): corre UN agente una y otra vez hasta que termina. El agente emite
/// <see cref="NodeSignal.Continue"/> mientras le queda por hacer (p.ej. más tool-calls) y
/// <see cref="NodeSignal.Done"/> cuando terminó. Es el molde del <c>AgenticCodeLoop</c> generalizado
/// (§3.3). El presupuesto (<see cref="ResiliencePolicy.MaxSteps"/>) acota los loops infinitos.
/// </summary>
public sealed class LoopStrategy(string agentId, NodeSignal continueOn = NodeSignal.Continue) : IControlStrategy
{
    public string Name => "loop";

    public Task<ControlDecision> NextAsync(NodeState state, CancellationToken ct = default)
    {
        // El último resultado de ESTE agente (no el global): cuando el loop está anidado dentro de otro
        // workflow, el historial trae también resultados del padre/hermanos — mirar el `[^1]` global haría
        // que el loop se corte por una señal ajena. Auto-scoped por agentId.
        var last = state.History.LastOrDefault(h => h.Response.AgentId == agentId);
        return Task.FromResult(last is null || last.Signal == continueOn
            ? ControlDecision.Run(agentId)          // primer paso o "seguí" → correr de nuevo
            : ControlDecision.Stop());              // terminó
    }
}
