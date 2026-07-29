namespace MiyuAgents.Workflows;

/// <summary>
/// Strategy trivial: corre los hijos EN ORDEN, uno por paso; termina (Done) cuando corrieron
/// todos. Avanza contando cuántos de SUS ids ya aparecen en el historial (por AgentId) → es
/// robusto a entradas sintéticas del historial (p.ej. la respuesta del Driver en un NeedsInput).
/// </summary>
public sealed class SequenceStrategy(IReadOnlyList<string> order) : IControlStrategy
{
    public string Name => "sequence";

    public Task<ControlDecision> NextAsync(NodeState state, CancellationToken ct = default)
    {
        var done = state.History.Count(h => order.Contains(h.Response.AgentId));
        return Task.FromResult(done < order.Count
            ? ControlDecision.Run(order[done])
            : ControlDecision.Stop());
    }
}
