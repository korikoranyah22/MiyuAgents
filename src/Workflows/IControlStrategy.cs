namespace MiyuAgents.Workflows;

/// <summary>
/// El "orquestador" de un Node compuesto: dado el estado del nodo, decide el próximo paso.
/// Generaliza <c>IRoundDecisionStrategy</c> (hoy atado a rondas de grupo) a cualquier dominio.
/// Impls previstas (§3.3 del spike): Converse (envuelve IRoundDecisionStrategy), Deliberate
/// (fases), Loop (ReAct sobre tool-calls), Sequence, Parallel, Perpetual. Es el punto donde
/// entran los bids (§3.6) — la Strategy los arbitra, no los honra a ciegas.
/// </summary>
public interface IControlStrategy
{
    string Name { get; }

    /// <summary>Decide qué correr a continuación (o terminar). Nunca lanza por lógica de decisión.</summary>
    Task<ControlDecision> NextAsync(NodeState state, CancellationToken ct = default);
}
