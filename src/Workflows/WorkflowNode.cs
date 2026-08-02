using Microsoft.Extensions.Logging;
using MiyuAgents.Core;

namespace MiyuAgents.Workflows;

/// <summary>
/// El RUNTIME de un Node compuesto (§3.4 del spike WORKFLOW-FRAMEWORK-00): corre el CONTROL-LOOP
/// sobre sus hijos (cada uno un <see cref="IAgent"/> → puede ser otro <see cref="WorkflowNode"/>,
/// recursión). La <see cref="IControlStrategy"/> decide quién sigue; el loop ejecuta (secuencial o
/// paralelo), maneja los signals (retry de Failed, bubble-up de NeedsReplanning/HandBack, ask al
/// Driver en NeedsInput, encola bids en RequestTurn) y respeta el presupuesto (<see cref="ResiliencePolicy"/>).
/// <para>Es un IAgent (vía <see cref="AgentBase{TResult}"/>) → desde afuera un workflow entero es
/// indistinguible de un agente (composición sin límite). Reserva de futuro: el scoping de eventos
/// para el trace por-lane (W7) + el checkpoint/event-sourcing del estado (§10 #2/#6).</para>
/// </summary>
public sealed class WorkflowNode : AgentBase<NodeResult>, ICompositeAgent, INodeAgent
{
    readonly IReadOnlyDictionary<string, IAgent> _children;
    readonly IControlStrategy _strategy;
    readonly ResiliencePolicy _policy;
    readonly IDriver? _driver;

    public override string    AgentId   { get; }
    public override string    AgentName { get; }
    public override AgentRole Role => AgentRole.Orchestration;
    public IReadOnlyList<IAgent> SubAgents { get; }

    public WorkflowNode(
        string id,
        IControlStrategy strategy,
        IReadOnlyDictionary<string, IAgent> children,
        ILogger<AgentBase<NodeResult>> logger,
        ResiliencePolicy? policy = null,
        IDriver? driver = null,
        string? name = null)
        : base(logger)
    {
        AgentId   = id;
        AgentName = name ?? id;
        _strategy = strategy;
        _children = children;
        _policy   = policy ?? ResiliencePolicy.Default;
        _driver   = driver;
        SubAgents = children.Values.ToList();
    }

    // ── Camino IAgent (ProcessAsync via AgentBase): arma un NodeState desde el ctx y corre el loop.
    protected override async Task<NodeResult?> ExecuteCoreAsync(AgentContext ctx, CancellationToken ct)
        => await RunNodeAsync(new NodeState { Input = ctx.UserMessage, Context = ctx }, ct);

    // ── Camino rico (recursión Node↔Node y entry directo desde el Driver/host).
    public async Task<NodeResult> RunNodeAsync(NodeState state, CancellationToken ct = default)
    {
        // El lane lo setea el PADRE al bajar a este nodo (NodeScope); top-level = el propio id.
        var lane = NodeScope.Current ?? AgentId;
        await Emit(TraceKind.NodeStart, lane);
        var result = await RunLoopAsync(state, lane, ct);
        await Emit(TraceKind.NodeEnd, lane, text: result.Signal.ToString());
        return result;
    }

    async Task<NodeResult> RunLoopAsync(NodeState state, string lane, CancellationToken ct)
    {
        var artifacts = new List<Artifact>();
        var transcript = new WorkflowTranscriptBuffer(
            _policy.MaxTranscriptEntries,
            _policy.MaxTranscriptTextLength);
        ControlDecision? pending = null;   // re-ruteo pedido por una ISignalReactiveStrategy (loop-back)

        for (var step = 0; step < _policy.MaxSteps; step++)
        {
            // Safe steering checkpoint: injected user messages become visible to the strategy and
            // every child from this control-loop iteration onward.
            state = WorkflowRunScope.Current?.Checkpoint(state) ?? state;
            var decision = pending ?? await _strategy.NextAsync(state, ct).WaitAsync(ct);
            pending = null;
            if (decision.IsTerminal)
                return Terminal(decision.Emit ?? NodeSignal.Done, artifacts, transcript.Snapshot());

            var settled = new List<NodeResult>();
            var newBids = new List<Bid>();
            var routed  = false;

            foreach (var (childId, produced) in await RunChildrenAsync(decision, state, lane, ct))
            {
                var result = produced;

                // Resiliencia: reintentar un hijo que falla, hasta MaxRetries.
                var attempts = 0;
                while (result.Signal == NodeSignal.Failed && attempts < _policy.MaxRetries)
                {
                    transcript.AddResult(
                        childId,
                        $"{lane}/{childId}",
                        attempts == 0 ? WorkflowTranscriptKind.ChildResult : WorkflowTranscriptKind.Retry,
                        result,
                        state.Round);
                    attempts++;
                    result = await RunOneAsync(childId, state, lane, ct);
                }

                transcript.AddResult(
                    childId,
                    $"{lane}/{childId}",
                    attempts == 0 ? WorkflowTranscriptKind.ChildResult : WorkflowTranscriptKind.Retry,
                    result,
                    state.Round);

                artifacts.AddRange(result.Artifacts);
                await Emit(TraceKind.ChildResult, $"{lane}/{childId}", actor: childId, text: result.Signal.ToString());

                switch (result.Signal)
                {
                    // Signals de routing/terminal: la strategy puede INTERCEPTARLOS (loop-back); si no, suben.
                    case NodeSignal.Failed:
                    case NodeSignal.NeedsReplanning:
                    case NodeSignal.HandBack:
                        settled.Add(result);
                        var reaction = _strategy is ISignalReactiveStrategy react
                            ? await react.OnChildSignalAsync(
                                state with { History = [.. state.History, .. settled] }, childId, result, ct).WaitAsync(ct)
                            : null;
                        if (reaction is null)
                            return Terminal(result.Signal, artifacts, transcript.Snapshot()); // default: sube al padre
                        if (reaction.IsTerminal)
                            return Terminal(reaction.Emit ?? result.Signal, artifacts, transcript.Snapshot());
                        pending = reaction;                                         // re-rutea (p.ej. vuelve a planning)
                        routed  = true;
                        break;
                    case NodeSignal.NeedsInput:
                        var ask = result.Ask ?? "";
                        transcript.AddDriverQuestion($"{lane}/{childId}", ask, state.Round);
                        using (WorkflowRunScope.Current?.BeginWaitingForInput(ask))
                        {
                            var answer = _driver is null
                                ? ""
                                : await _driver.AnswerAsync(ask, state, ct).WaitAsync(ct);
                            transcript.AddDriverAnswer($"{lane}/{childId}", answer, state.Round);
                            settled.Add(result);
                            settled.Add(DriverAnswer(answer));                      // la respuesta entra al historial
                        }
                        break;
                    case NodeSignal.RequestTurn:
                        newBids.Add(new Bid(childId));                              // §3.6: bidea para el próximo turno
                        settled.Add(result);
                        break;
                    default:                                                        // Done / Continue
                        settled.Add(result);
                        break;
                }

                if (routed) break;
            }

            // Cierre del paso: historial += lo resuelto; bids = (los viejos que NO corrieron) + los nuevos.
            var ran = decision.RunNext.ToHashSet();
            state = state with
            {
                History = [.. state.History, .. settled],
                Bids    = [.. state.Bids.Where(b => !ran.Contains(b.NodeId)), .. newBids],
                Round   = state.Round + 1,
            };
        }

        // Presupuesto agotado → falla acotada (anti-cuelgue; §5).
        return Terminal(NodeSignal.Failed, artifacts, transcript.Snapshot());
    }

    // ── helpers del loop ───────────────────────────────────────────────────────
    async Task<IReadOnlyList<(string Id, NodeResult Result)>> RunChildrenAsync(
        ControlDecision d, NodeState state, string lane, CancellationToken ct)
    {
        if (d.Parallel)
            return await Task.WhenAll(d.RunNext.Select(async id => (id, await RunOneAsync(id, state, lane, ct))));

        var results = new List<(string, NodeResult)>(d.RunNext.Count);
        foreach (var id in d.RunNext)
            results.Add((id, await RunOneAsync(id, state, lane, ct)));
        return results;
    }

    async Task<NodeResult> RunOneAsync(string id, NodeState state, string lane, CancellationToken ct)
    {
        if (!_children.TryGetValue(id, out var child))
            return Fail(id, $"no existe el hijo '{id}'");

        // Bajamos al hijo extendiendo el LANE (path) → sus eventos de trace quedan tagueados con la
        // ruta completa (recursión de profundidad arbitraria, §8.4). AsyncLocal → aislado en paralelo.
        using (NodeScope.Enter($"{lane}/{id}"))
        {
            NodeResult result;
            if (child is INodeAgent node)                       // Node rico (recursión) → RunNodeAsync
                result = await node.RunNodeAsync(state, ct).WaitAsync(ct);
            else                                                // IAgent común → ProcessAsync + envolver
            {
                // Preserve the original multimodal/identity context across nested nodes. The record
                // copy keeps its shared accumulator while exposing steering and newly attached media.
                var ctx = state.Context is { } source
                    ? source with { UserMessage = state.EffectiveInput, Attachments = state.EffectiveAttachments }
                    : AgentContext.For("workflow", Guid.NewGuid().ToString("N"), state.EffectiveInput) with
                    {
                        Attachments = state.EffectiveAttachments,
                    };
                result  = AsNodeResult(await child.ProcessAsync(ctx, ct).WaitAsync(ct));
            }

            // El strategy direcciona a los hijos por su ROSTER-ID (la key del roster), que puede diferir
            // del AgentId propio del hijo (un mismo agente reusado bajo varios roles) → re-estampamos el
            // resultado con el roster-id para que el matching del historial sea estable e independiente.
            return result with { Response = result.Response with { AgentId = id } };
        }
    }

    // Emite un evento de trace al sink ambiente (si hay). null → no-op, cero overhead.
    Task Emit(TraceKind kind, string lane, string? actor = null, string? text = null)
    {
        var sink = NodeTrace.Current;
        return sink is null
            ? Task.CompletedTask
            : sink.EmitAsync(new TraceEvent(AgentId, lane, kind, actor, text, At: DateTimeOffset.UtcNow));
    }

    // Un IAgent común no habla de signals → Ok=Done, Error=Failed. (Si por convención dejó un
    // NodeResult en Data —p.ej. otro WorkflowNode invocado por ProcessAsync— se respeta.)
    static NodeResult AsNodeResult(AgentResponse r) =>
        r.Data as NodeResult
        ?? NodeResult.From(r, r.Status == AgentStatus.Error ? NodeSignal.Failed : NodeSignal.Done);

    NodeResult Terminal(
        NodeSignal signal,
        IReadOnlyList<Artifact> artifacts,
        IReadOnlyList<WorkflowTranscriptEntry> transcript) => new()
    {
        Response = new AgentResponse
        {
            AgentId = AgentId, AgentName = AgentName, Role = Role,
            Data = artifacts, Status = signal == NodeSignal.Failed ? AgentStatus.Error : AgentStatus.Ok,
        },
        Signal    = signal,
        Artifacts = artifacts,
        Transcript = transcript,
    };

    NodeResult Fail(string id, string msg) => new()
    {
        Response = new AgentResponse { AgentId = id, AgentName = id, Role = Role, Status = AgentStatus.Error, ErrorMessage = msg },
        Signal   = NodeSignal.Failed,
    };

    NodeResult DriverAnswer(string answer) => new()
    {
        Response = new AgentResponse { AgentId = "driver", AgentName = "Driver", Role = AgentRole.Orchestration, Data = answer },
        Signal   = NodeSignal.Done,
    };
}
