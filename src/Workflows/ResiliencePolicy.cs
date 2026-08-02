namespace MiyuAgents.Workflows;

/// <summary>
/// Presupuesto + reintentos que el control-loop hace cumplir (§5 del spike — "que no se rindan
/// ante agentes vagos"). <paramref name="MaxSteps"/> acota el loop (anti-cuelgue → si se excede,
/// el nodo termina con <see cref="NodeSignal.Failed"/>). <paramref name="MaxRetries"/> reintenta
/// un hijo que devuelve <see cref="NodeSignal.Failed"/> antes de propagar el fallo.
/// </summary>
public sealed record ResiliencePolicy(
    int MaxSteps = 50,
    int MaxRetries = 0,
    int MaxTranscriptEntries = 200,
    int MaxTranscriptTextLength = 1_000)
{
    public static readonly ResiliencePolicy Default = new();
}
