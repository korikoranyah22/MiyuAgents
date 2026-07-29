namespace MiyuAgents.Workflows;

/// <summary>
/// Un Node como DATA (§6 del spike). <paramref name="Members"/> = el roster ordenado de ids que la
/// strategy direcciona; cada id resuelve a un AGENTE del registry O al <see cref="Id"/> de un
/// sub-<see cref="NodeSpec"/> en <paramref name="Children"/> (recursión → sub-workflows).
/// <paramref name="Strategy"/> es el nombre que el registry mapea a una <see cref="IControlStrategy"/>.
/// Instanciar un árbol desde esto es barato → editable/recargable EN CALIENTE (sin rebuild, sin down/up).
/// </summary>
public sealed record NodeSpec(
    string                                Id,
    string                                Strategy,
    IReadOnlyList<string>                 Members,
    IReadOnlyList<NodeSpec>?              Children = null,
    IReadOnlyDictionary<string, string>?  Params   = null,
    ResiliencePolicy?                     Budget   = null);

/// <summary>Un workflow completo como DATA: metadata + el árbol de Nodes (raíz). Se shippea, se
/// edita en runtime, se expone como skill (interna) y como tool MCP (externa).</summary>
public sealed record WorkflowSpec(
    string   Id,
    string   DisplayName,
    string   Description,
    NodeSpec Root);
