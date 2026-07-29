namespace MiyuAgents.Workflows;

/// <summary>
/// El LANE ambiente (AsyncLocal): el path jerárquico del nodo en ejecución ("outer/inner/dev-3").
/// El <c>WorkflowNode</c> lo extiende al bajar a un hijo → los eventos de trace de cualquier nivel
/// quedan tagueados con su path (§8.4). Mismo molde que el <c>CodeBroadcastScope</c> del host, pero
/// promovido al framework y con PATH (no un solo taskId) para recursión de profundidad arbitraria.
/// AsyncLocal → aislado por rama en el fan-out paralelo.
/// </summary>
public static class NodeScope
{
    static readonly AsyncLocal<string?> _lane = new();

    public static string? Current => _lane.Value;

    public static IDisposable Enter(string lane)
    {
        var prev = _lane.Value;
        _lane.Value = lane;
        return new Pop(prev);
    }

    sealed class Pop(string? prev) : IDisposable
    {
        public void Dispose() => _lane.Value = prev;
    }
}

/// <summary>
/// El SINK de trace ambiente (AsyncLocal): se setea UNA vez en la raíz (<c>NodeTrace.Begin(sink)</c>)
/// y todos los nodos anidados emiten ahí, sin threadear el sink por el árbol. null = sin trace.
/// </summary>
public static class NodeTrace
{
    static readonly AsyncLocal<INodeTraceSink?> _sink = new();

    public static INodeTraceSink? Current => _sink.Value;

    public static IDisposable Begin(INodeTraceSink sink)
    {
        var prev = _sink.Value;
        _sink.Value = sink;
        return new Pop(prev);
    }

    sealed class Pop(INodeTraceSink? prev) : IDisposable
    {
        public void Dispose() => _sink.Value = prev;
    }
}
