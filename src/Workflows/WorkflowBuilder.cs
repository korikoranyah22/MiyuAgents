using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MiyuAgents.Core;

namespace MiyuAgents.Workflows;

/// <summary>
/// Instancia el árbol de <see cref="WorkflowNode"/>s desde un <see cref="WorkflowSpec"/> (data),
/// resolviendo agentes y strategies por el <see cref="IWorkflowRegistry"/>. Es BARATO → rebuild en
/// caliente al editar un spec (hot-authoring, §6): no hay rebuild del binario ni down/up.
/// </summary>
public static class WorkflowBuilder
{
    public static WorkflowNode Build(
        WorkflowSpec spec, IWorkflowRegistry registry, ILogger<AgentBase<NodeResult>>? logger = null)
        => BuildNode(spec.Root, registry, logger ?? NullLogger<AgentBase<NodeResult>>.Instance);

    public static WorkflowNode BuildNode(
        NodeSpec spec, IWorkflowRegistry registry, ILogger<AgentBase<NodeResult>> logger)
    {
        var children = new Dictionary<string, IAgent>(StringComparer.Ordinal);
        foreach (var memberId in spec.Members)
        {
            var childSpec = spec.Children?.FirstOrDefault(c => c.Id == memberId);
            children[memberId] = childSpec is not null
                ? BuildNode(childSpec, registry, logger)                            // recursión (sub-workflow)
                : registry.ResolveAgent(memberId)
                    ?? throw new InvalidOperationException($"agente no registrado: '{memberId}'");
        }

        return new WorkflowNode(
            spec.Id, registry.CreateStrategy(spec), children, logger, spec.Budget ?? registry.DefaultPolicy);
    }
}
