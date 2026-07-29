namespace MiyuAgents.Workflows;

/// <summary>Qué clase de evento del árbol de ejecución (§8.3 del spike). El host lo mapea a los tipos
/// del transcript (reason/speech/tool/exec) al enchufar su sink. <see cref="TraceKind.Widget"/> lleva un
/// payload TIPADO (JSON en <see cref="TraceEvent.Data"/>) que el front renderiza rico (imagen/diff/tabla…)
/// — simétrico a los plugins (config tipada de ENTRADA); esto es render tipado de SALIDA.</summary>
public enum TraceKind { NodeStart, NodeEnd, ChildResult, Reason, Response, Tool, Exec, Widget }

/// <summary>
/// Un evento del TRACE del árbol de workflows. <paramref name="Lane"/> es el PATH jerárquico del nodo
/// ("outer/inner/dev-3") → habilita la recursión de profundidad arbitraria en el render/persistencia
/// (§8.4). Domain-neutral: el host lo persiste (event-sourcing §10 #6) y/o lo streamea (SignalR).
/// <paramref name="Data"/> = payload OPACO (JSON) para <see cref="TraceKind.Widget"/>; el framework no lo
/// interpreta (el host/front deciden el shape por <paramref name="Actor"/> = tipo de widget).
/// </summary>
public sealed record TraceEvent(
    string         NodeId,
    string         Lane,
    TraceKind      Kind,
    string?        Actor     = null,
    string?        Text      = null,
    bool           Streaming = false,
    DateTimeOffset At        = default,
    string?        Data      = null);

/// <summary>
/// El PORT de trace (§8.3, gap W7): el runtime del Node lo alimenta; el host lo implementa reusando
/// el transcript (persistencia, sobrevive F5) + el broadcaster (streaming live). El framework queda
/// agnóstico de Eventuous/SignalR. null = sin trace (comportamiento base, cero overhead).
/// </summary>
public interface INodeTraceSink
{
    Task EmitAsync(TraceEvent ev, CancellationToken ct = default);
}

/// <summary>Sink FAKE en memoria (tests): junta los eventos en orden. Thread-safe (fan-out paralelo).</summary>
public sealed class InMemoryTraceSink : INodeTraceSink
{
    readonly List<TraceEvent> _events = [];
    readonly Lock _gate = new();

    public IReadOnlyList<TraceEvent> Events { get { lock (_gate) return _events.ToList(); } }

    public Task EmitAsync(TraceEvent ev, CancellationToken ct = default)
    {
        lock (_gate) _events.Add(ev);
        return Task.CompletedTask;
    }
}
