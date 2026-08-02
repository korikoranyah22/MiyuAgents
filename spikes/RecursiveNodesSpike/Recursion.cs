using MiyuAgents.Core;
using MiyuAgents.Core.Events;
using MiyuAgents.Workflows;

namespace RecursiveNodesSpike;

// ─────────────────────────────────────────────────────────────────────────────
// API EXPLORATORIA del spike (T003) — NO es API pública del framework (§5.4 del
// README de este spike). Se construye SOBRE las primitivas reales de
// MiyuAgents.Workflows (INodeAgent, NodeResult, NodeSignal, NodeState, Artifact)
// y agrega, LOCAL al spike, lo que al framework le falta para expresar un nodo
// recursivo como una función recursiva:
//   1. auto-referencia DIFERIDA  (el cuerpo resuelve el próximo paso en runtime);
//   2. cota de profundidad + trampolín ITERATIVO (stack-safe);
//   3. detección de ciclos por (id, input) en la cadena actual;
//   4. frame por llamada (Input, Depth, Carry) → propagación de contexto/estado;
//   5. caso base mapeado a NodeResult (Done) y corte a Failed.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// El frame de UNA llamada recursiva: el input de esta llamada, la profundidad
/// (0 = raíz) y el estado/contexto que se propaga hacia abajo y se pliega hacia
/// arriba (el "carry" de la función recursiva — p.ej. el acumulador de una suma
/// tail-recursiva).
/// </summary>
public sealed record RecursiveFrame(string Input, int Depth, object? Carry = null);

/// <summary>
/// La decisión del cuerpo para un frame, espejo de una función recursiva:
/// <see cref="Base"/> = caso base (termina con un <see cref="NodeResult"/>),
/// <see cref="Next"/> = paso recursivo (la CONTINUACIÓN: volver a invocar —
/// normalmente al MISMO nodo — con un input reducido y un carry actualizado).
/// El sketch modela tail-recursion; la variante no-tail (post-procesar el
/// resultado del hijo, p.ej. <c>n * fact(n-1)</c>) necesita una continuación que
/// RECIBA el resultado del hijo — gap documentado en §4.4 del README, no
/// implementado acá.
/// </summary>
public abstract record RecursionDecision
{
    /// <summary>Caso base: termina la recursión con este resultado (Done).</summary>
    public sealed record Base(NodeResult Result) : RecursionDecision;

    /// <summary>Paso recursivo (la continuación): siguiente input (reducido) + carry actualizado.</summary>
    public sealed record Next(string NextInput, object? NextCarry) : RecursionDecision;
}

/// <summary>
/// Presupuesto de la recursión: cota de profundidad (stack-safety). La recursión
/// legítima es bien-fundada (cada paso reduce el input); la cota corta la que no
/// lo es, igual que <see cref="MiyuAgents.Workflows.ResiliencePolicy.MaxSteps"/>
/// corta un control-loop que nunca termina.
/// </summary>
public sealed record RecursionBudget(int MaxDepth = 64)
{
    public static readonly RecursionBudget Default = new();
}

/// <summary>
/// Nodo recursivo EXPLORATORIO (spike T003). Es un <see cref="INodeAgent"/> real:
/// habla <see cref="NodeResult"/>, así que un
/// <see cref="MiyuAgents.Workflows.WorkflowNode"/> lo aceptaría como hijo sin
/// cambios. La diferencia con un nodo común: el cuerpo puede devolver
/// <see cref="RecursionDecision.Next"/> → el runner vuelve a invocar al nodo con
/// un input reducido (auto-referencia DIFERIDA: la resolución del próximo paso
/// ocurre en runtime, no en el constructor — que es donde el framework hoy cierra
/// el roster de hijos).
/// El runner es un TRAMPOLÍN ITERATIVO: nunca anida una invocación dentro de otra
/// (stack-safe), corta por <see cref="RecursionBudget.MaxDepth"/> y detecta ciclos
/// (mismo id + mismo input repetido en la cadena actual) devolviendo
/// <see cref="NodeSignal.Failed"/>.
/// </summary>
public sealed class RecursiveWorkflowNode : INodeAgent
{
    readonly Func<RecursiveFrame, RecursionDecision> _body;
    readonly RecursionBudget _budget;
    readonly Action<string>? _trace;

    public RecursiveWorkflowNode(
        string id,
        Func<RecursiveFrame, RecursionDecision> body,
        RecursionBudget? budget = null,
        Action<string>? trace = null)
    {
        AgentId   = id;
        AgentName = id;
        _body     = body;
        _budget   = budget ?? RecursionBudget.Default;
        _trace    = trace;
    }

    public string    AgentId   { get; }
    public string    AgentName { get; }
    public AgentRole Role      => AgentRole.Orchestration;

    // ── Camino rico (igual que WorkflowNode): arma el frame raíz desde el NodeState. ──
    public Task<NodeResult> RunNodeAsync(NodeState state, CancellationToken ct = default)
        => Task.FromResult(Run(new RecursiveFrame(state.Input, Depth: 0), ct));

    NodeResult Run(RecursiveFrame first, CancellationToken ct)
    {
        var artifacts = new List<Artifact>();
        var chain     = new HashSet<string>(StringComparer.Ordinal);   // fingerprints (id::input) de la cadena actual
        var frame     = first;

        for (var depth = 0; ; depth++)
        {
            ct.ThrowIfCancellationRequested();

            // 2) cota de profundidad → Failed. El trampolín nunca creció la pila: el corte es limpio.
            if (depth > _budget.MaxDepth)
                return Fail($"profundidad máxima excedida ({_budget.MaxDepth})", artifacts, trace: true);

            // 3) detección de ciclos: mismo nodo + mismo input ya vistos en ESTA cadena → Failed.
            var fingerprint = $"{AgentId}::{frame.Input}";
            if (!chain.Add(fingerprint))
                return Fail($"ciclo detectado: '{fingerprint}' se repite en la misma cadena", artifacts, trace: true);

            _trace?.Invoke($"[{AgentId}] enter {frame.Input} depth={depth} carry={frame.Carry ?? "<null>"}");

            // 5) caso base → el NodeResult final (Done) con los artefactos acumulados.
            var decision = _body(frame);
            if (decision is RecursionDecision.Base b)
            {
                _trace?.Invoke($"[{AgentId}] base {frame.Input} → {b.Result.Signal}");
                artifacts.AddRange(b.Result.Artifacts);
                return b.Result with { Artifacts = artifacts };
            }

            // 4) paso recursivo: input reducido + carry → siguiente iteración del trampolín.
            var next = (RecursionDecision.Next)decision;
            _trace?.Invoke($"[{AgentId}] recurse {frame.Input} → {next.NextInput}");
            frame = frame with { Input = next.NextInput, Carry = next.NextCarry, Depth = depth + 1 };
        }
    }

    NodeResult Fail(string reason, IReadOnlyList<Artifact> artifacts, bool trace = false)
    {
        if (trace) _trace?.Invoke($"[{AgentId}] Failed: {reason}");
        return new()
        {
            Response = new AgentResponse
            {
                AgentId = AgentId, AgentName = AgentName, Role = Role,
                Status = AgentStatus.Error, ErrorMessage = reason,
            },
            Signal    = NodeSignal.Failed,
            Artifacts = artifacts,
        };
    }

    // ── Contrato IAgent: este nodo se invoca por el camino rico (RunNodeAsync). ──
    public Task<AgentResponse> ProcessAsync(AgentContext ctx, CancellationToken ct = default)
        => throw new NotSupportedException("el nodo recursivo se invoca por RunNodeAsync");

#pragma warning disable CS0067 // eventos del contrato IAgent, no se disparan en el spike
    public event AsyncEventHandler<MessageReceivedEventArgs>?       OnMessageReceived;
    public event AsyncEventHandler<LlmCallRequestedEventArgs>?      OnLLMCallRequested;
    public event AsyncEventHandler<LlmCallRespondedEventArgs>?      OnLLMCallResponded;
    public event AsyncEventHandler<AgentResponseProducedEventArgs>? OnResponseProduced;
    public event AsyncEventHandler<AgentErrorEventArgs>?            OnError;
#pragma warning restore CS0067
}
