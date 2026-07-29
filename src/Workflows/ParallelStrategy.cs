namespace MiyuAgents.Workflows;

/// <summary>
/// Strategy trivial: corre TODOS los hijos EN PARALELO (fan-out) en un solo paso, y termina
/// (Done). Base del "10 agentes leen código y resumen" (§8.2); el fan-in lo hace un Node
/// sintetizador aparte. Detecta que ya corrieron mirando si alguno de sus ids está en el historial.
/// </summary>
public sealed class ParallelStrategy(IReadOnlyList<string> ids) : IControlStrategy
{
    public string Name => "parallel";

    public Task<ControlDecision> NextAsync(NodeState state, CancellationToken ct = default)
        => Task.FromResult(state.History.Any(h => ids.Contains(h.Response.AgentId))
            ? ControlDecision.Stop()
            : new ControlDecision(ids, Parallel: true));
}
