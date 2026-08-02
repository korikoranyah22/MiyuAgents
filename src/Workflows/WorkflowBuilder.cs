using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MiyuAgents.Core;

namespace MiyuAgents.Workflows;

/// <summary>
/// Builds a tree of <see cref="WorkflowNode"/> instances from runtime-authored data. Structural
/// NodeSpec cycles are rejected explicitly; intentional functional recursion belongs in a
/// <see cref="RecursiveWorkflowNode{TState}"/> registered as a leaf agent.
/// </summary>
public static class WorkflowBuilder
{
    public static WorkflowNode Build(
        WorkflowSpec spec,
        IWorkflowRegistry registry,
        ILogger<AgentBase<NodeResult>>? logger = null)
        => BuildNode(spec.Root, registry, logger ?? NullLogger<AgentBase<NodeResult>>.Instance);

    public static WorkflowNode BuildNode(
        NodeSpec spec,
        IWorkflowRegistry registry,
        ILogger<AgentBase<NodeResult>> logger)
        => BuildNode(spec, registry, logger, [], new HashSet<NodeSpec>(ReferenceEqualityComparer.Instance));

    static WorkflowNode BuildNode(
        NodeSpec spec,
        IWorkflowRegistry registry,
        ILogger<AgentBase<NodeResult>> logger,
        IReadOnlyList<string> path,
        HashSet<NodeSpec> active)
    {
        if (!active.Add(spec))
            throw new InvalidOperationException(
                $"recursive NodeSpec cycle detected: {string.Join(" -> ", [.. path, spec.Id])}. " +
                "Use RecursiveWorkflowNode<TState> for intentional self-recursion.");

        var duplicateChild = spec.Children?
            .GroupBy(c => c.Id, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1)?.Key;
        if (duplicateChild is not null)
            throw new InvalidOperationException($"duplicate child NodeSpec id '{duplicateChild}' under '{spec.Id}'");

        try
        {
            var children = new Dictionary<string, IAgent>(StringComparer.Ordinal);
            foreach (var memberId in spec.Members)
            {
                var childSpec = spec.Children?.FirstOrDefault(c => c.Id == memberId);
                children[memberId] = childSpec is not null
                    ? BuildNode(childSpec, registry, logger, [.. path, spec.Id], active)
                    : registry.ResolveAgent(memberId)
                        ?? throw new InvalidOperationException($"agent not registered: '{memberId}'");
            }

            return new WorkflowNode(
                spec.Id,
                registry.CreateStrategy(spec),
                children,
                logger,
                spec.Budget ?? registry.DefaultPolicy);
        }
        finally
        {
            active.Remove(spec);
        }
    }
}
