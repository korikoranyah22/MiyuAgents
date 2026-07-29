using System.Collections.Concurrent;

namespace MiyuAgents.Workflows;

/// <summary>
/// Store de <see cref="WorkflowSpec"/>s. El "hot-authoring" (§6) sale de acá: editar/guardar un spec
/// con la app corriendo → el próximo <see cref="WorkflowBuilder.Build"/> usa el nuevo, sin rebuild.
/// El host lo respalda como quiera (dir <c>.workflows/</c>, Postgres, event-sourcing §10 #6).
/// </summary>
public interface IWorkflowStore
{
    void Save(WorkflowSpec spec);
    WorkflowSpec? Get(string id);
    IReadOnlyList<WorkflowSpec> List();
    bool Remove(string id);
}

/// <summary>Impl en memoria (default para tests y arranque). Thread-safe.</summary>
public sealed class InMemoryWorkflowStore : IWorkflowStore
{
    readonly ConcurrentDictionary<string, WorkflowSpec> _specs = new();

    public void Save(WorkflowSpec spec) => _specs[spec.Id] = spec;
    public WorkflowSpec? Get(string id) => _specs.GetValueOrDefault(id);
    public IReadOnlyList<WorkflowSpec> List() => _specs.Values.ToList();
    public bool Remove(string id) => _specs.TryRemove(id, out _);
}
