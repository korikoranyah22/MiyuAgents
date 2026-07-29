namespace MiyuAgents.Workflows;

/// <summary>
/// La decisión de una <see cref="IControlStrategy"/>: qué hijos correr ahora (0 = terminar),
/// cómo (secuencial o paralelo) y —si termina— con qué <see cref="NodeSignal"/> sube al padre.
/// Mirror de <c>OrchestratorDecision</c> (rondas de grupo), generalizado a cualquier Node.
/// </summary>
public sealed record ControlDecision(
    IReadOnlyList<string> RunNext,
    bool        Parallel = false,
    NodeSignal? Emit     = null)
{
    /// <summary>Sin hijos que correr → el nodo termina (subiendo <see cref="Emit"/> ?? Done).</summary>
    public bool IsTerminal => RunNext.Count == 0;

    /// <summary>Terminar el nodo con el signal dado.</summary>
    public static ControlDecision Stop(NodeSignal emit = NodeSignal.Done) => new([], false, emit);

    /// <summary>Correr estos hijos EN SECUENCIA.</summary>
    public static ControlDecision Run(params string[] ids) => new(ids, false);

    /// <summary>Correr estos hijos EN PARALELO (fan-out).</summary>
    public static ControlDecision RunParallel(params string[] ids) => new(ids, true);
}
