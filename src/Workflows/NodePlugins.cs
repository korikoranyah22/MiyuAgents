namespace MiyuAgents.Workflows;

/// <summary>
/// Los PLUGINS ambiente de la corrida (AsyncLocal, mismo molde que <see cref="NodeTrace"/>): el
/// entorno/configuración que los nodos y subnodos necesitan — a cualquier profundidad recursiva —
/// sin threadear todo por constructores. El runner los engancha una vez al arrancar
/// (<c>NodePlugins.Begin(plugin, …)</c>); un nodo lee <c>NodePlugins.Get&lt;MiPlugin&gt;()</c>.
/// Sin plugins = lista vacía (los nodos degradan a su comportamiento standalone).
/// </summary>
public static class NodePlugins
{
    static readonly AsyncLocal<IReadOnlyList<IWorkflowPlugin>?> _current = new();

    public static IReadOnlyList<IWorkflowPlugin> Current => _current.Value ?? [];

    /// <summary>El plugin de tipo <typeparamref name="T"/> de la corrida actual (null si no hay).</summary>
    public static T? Get<T>() where T : class, IWorkflowPlugin => Current.OfType<T>().FirstOrDefault();

    public static IDisposable Begin(params IWorkflowPlugin[] plugins)
    {
        var prev = _current.Value;
        _current.Value = plugins;
        return new Pop(prev);
    }

    sealed class Pop(IReadOnlyList<IWorkflowPlugin>? prev) : IDisposable
    {
        public void Dispose() => _current.Value = prev;
    }
}
