using MiyuAgents.Core;

namespace MiyuAgents.Workflows;

/// <summary>
/// <see cref="IWorkflowRegistry"/> por defecto: un diccionario de agentes + fábricas de strategy por
/// nombre. Trae las built-in (sequence/parallel/loop/converse/plan-execute); el host puede sumar/
/// pisar fábricas (p.ej. "deliberate" con fases del dominio, o strategies propias) vía
/// <paramref name="extraStrategies"/> — así el authoring es extensible sin tocar el framework.
/// </summary>
public sealed class WorkflowRegistry : IWorkflowRegistry
{
    readonly IReadOnlyDictionary<string, IAgent> _agents;
    readonly Dictionary<string, Func<NodeSpec, IControlStrategy>> _strategies;

    public ResiliencePolicy DefaultPolicy { get; }

    public WorkflowRegistry(
        IReadOnlyDictionary<string, IAgent> agents,
        IReadOnlyDictionary<string, Func<NodeSpec, IControlStrategy>>? extraStrategies = null,
        ResiliencePolicy? defaultPolicy = null)
    {
        _agents       = agents;
        DefaultPolicy = defaultPolicy ?? ResiliencePolicy.Default;
        _strategies = new(StringComparer.OrdinalIgnoreCase)
        {
            ["sequence"]     = s => new SequenceStrategy(s.Members),
            ["parallel"]     = s => new ParallelStrategy(s.Members),
            ["loop"]         = s => new LoopStrategy(s.Members[0]),
            ["converse"]     = s => new ConverseStrategy(s.Members, IntParam(s, "maxRounds", 6)),
            ["plan-execute"] = s => new PlanExecuteStrategy(s.Members[0], s.Members[1], IntParam(s, "maxReplans", 2)),
        };
        if (extraStrategies is not null)
            foreach (var (k, v) in extraStrategies) _strategies[k] = v;
    }

    public IAgent? ResolveAgent(string id) => _agents.GetValueOrDefault(id);

    public IControlStrategy CreateStrategy(NodeSpec spec)
        => _strategies.TryGetValue(spec.Strategy, out var factory)
            ? factory(spec)
            : throw new InvalidOperationException($"strategy desconocida: '{spec.Strategy}'");

    static int IntParam(NodeSpec s, string key, int fallback)
        => s.Params is not null && s.Params.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : fallback;
}
