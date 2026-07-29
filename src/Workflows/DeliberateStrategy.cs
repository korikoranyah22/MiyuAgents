namespace MiyuAgents.Workflows;

/// <summary>Una FASE de una deliberación: qué roster corre y cómo. Ej.: comprensión (todos en
/// paralelo), síntesis (uno), debate (los debatientes), review (el revisor).</summary>
public sealed record Phase(IReadOnlyList<string> Run, bool Parallel = false, string? Name = null);

/// <summary>
/// Strategy "Deliberate": corre una SECUENCIA de fases (§3.3). Es el <c>DomainTeamOrchestrator</c>
/// generalizado: opinan → sintetizan → debaten → revisan → … El cursor de fase es
/// <see cref="NodeState.Round"/> (una iteración del control-loop = una fase), así es stateless sobre
/// el estado (sobrevive checkpoint/replay). Cada fase puede correr en secuencia o paralelo.
/// </summary>
public sealed class DeliberateStrategy(IReadOnlyList<Phase> phases) : IControlStrategy
{
    public string Name => "deliberate";

    public Task<ControlDecision> NextAsync(NodeState state, CancellationToken ct = default)
        => Task.FromResult(state.Round < phases.Count
            ? new ControlDecision(phases[state.Round].Run, phases[state.Round].Parallel)
            : ControlDecision.Stop());
}
