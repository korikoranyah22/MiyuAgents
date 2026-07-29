namespace MiyuAgents.Workflows;

/// <summary>
/// Lo que una tool recibe para interactuar con el entorno (§4): acceso al <see cref="IWorkflowPlugin"/>
/// de la sesión (sandbox/playground/ninguno). El host puede exponer más capacidades acá (broker MCP,
/// etc.) sin cambiar el contrato de <see cref="ITool"/>.
/// </summary>
public interface IToolHost
{
    IWorkflowPlugin? Plugin { get; }
    T? PluginAs<T>() where T : class, IWorkflowPlugin;
}

/// <summary>
/// Tool EJECUTABLE (§4). El framework define el port; las impls concretas (código: read/write/exec/
/// apply_patch; datos: broker MCP) son del host / Spike 2. Un agente arma su toolset con estos + los
/// que expone MCP (adapter del host). <see cref="ExecuteAsync"/> toma args JSON y devuelve un string.
/// </summary>
public interface ITool
{
    string  Name        { get; }
    string? Description { get; }
    Task<string> ExecuteAsync(string argsJson, IToolHost host, CancellationToken ct = default);
}

/// <summary>Impl default de <see cref="IToolHost"/>: envuelve un plugin opcional.</summary>
public sealed class ToolHost(IWorkflowPlugin? plugin = null) : IToolHost
{
    public IWorkflowPlugin? Plugin => plugin;
    public T? PluginAs<T>() where T : class, IWorkflowPlugin => plugin as T;
}
