using System.Collections.Concurrent;

namespace MiyuAgents.Workflows;

/// <summary>
/// El driver de SISTEMA — completa la tríada human/character/system. Para corridas disparadas
/// MECÁNICAMENTE (un botón, un stage automático, un cron, una API) donde no hay humano esperando ni
/// personaje conversando: NUNCA bloquea. Responde cada <c>NeedsInput</c> con la siguiente respuesta
/// provista (semillas en orden, p.ej. la escena que ya vino en el disparo) y, agotadas, con la
/// directiva de criterio propio (<paramref name="fallback"/>) — el workflow sigue solo.
/// </summary>
public sealed class SystemDriver(IReadOnlyList<string>? answers = null, string? fallback = null) : IDriver
{
    readonly ConcurrentQueue<string> _answers = new(answers ?? []);
    readonly string _fallback = fallback ?? "(disparo de sistema, sin operador — decidí con tu propio criterio)";

    public SystemDriver(params string[] answers) : this((IReadOnlyList<string>)answers) { }

    /// <summary>Cuántas preguntas respondió (semillas + fallbacks) — útil para tests/telemetría.</summary>
    public int Answered { get; private set; }

    public Task<string> AnswerAsync(string ask, NodeState state, CancellationToken ct = default)
    {
        Answered++;
        return Task.FromResult(_answers.TryDequeue(out var a) ? a : _fallback);
    }
}
