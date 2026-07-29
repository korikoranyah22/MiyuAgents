using MiyuAgents.Core;

namespace MiyuAgents.Workflows;

/// <summary>
/// Un <see cref="IAgent"/> que produce un <see cref="NodeResult"/> RICO (signal/artifacts), no
/// sólo un <see cref="AgentResponse"/>. El control-loop lo PREFIERE (<see cref="RunNodeAsync"/>);
/// un IAgent común se envuelve (Status → Done/Failed). <c>WorkflowNode</c> lo implementa → la
/// recursión (Node dentro de Node) va por este camino, pasando el <see cref="NodeState"/> sin
/// necesidad de un AgentContext. "Node = IAgent" se mantiene: <c>INodeAgent : IAgent</c>.
/// </summary>
public interface INodeAgent : IAgent
{
    Task<NodeResult> RunNodeAsync(NodeState state, CancellationToken ct = default);
}
