using MiyuAgents.Core;

namespace MiyuAgents.Workflows;

/// <summary>
/// Resuelve, por id, las piezas que un <see cref="NodeSpec"/> referencia: los AGENTES hoja y las
/// STRATEGIES (por nombre + params). Es el punto de extensión del authoring: registrar un agente o
/// una strategy nuevos = enchufar un id → el spec ya los puede usar, sin recompilar el framework.
/// </summary>
public interface IWorkflowRegistry
{
    IAgent? ResolveAgent(string id);
    IControlStrategy CreateStrategy(NodeSpec spec);
    ResiliencePolicy DefaultPolicy { get; }
}
