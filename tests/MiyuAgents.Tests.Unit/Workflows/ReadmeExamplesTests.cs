using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MiyuAgents.Core;
using MiyuAgents.Core.Events;
using MiyuAgents.Workflows;
using Xunit;

namespace MiyuAgents.Tests.Unit.Workflows;

// ── Los DOS ejemplos de la sección "Workflows y nodos" del README, compilados y corridos
//    contra la API real (T002). Las hojas son instrumentadas (contadores Interlocked) para
//    poder ASSERTAR la concurrencia y los reintentos que la sección documenta. ──────────────
#pragma warning disable CS0067

// Hoja genérica guionada por nº de invocación (thread-safe: Interlocked).
file sealed class ScriptNode(string id, Func<int, NodeResult> script) : INodeAgent
{
    int _calls;

    public int Calls => Volatile.Read(ref _calls);
    public string AgentId   => id;
    public string AgentName => id;
    public AgentRole Role   => AgentRole.Custom;

    public Task<NodeResult> RunNodeAsync(NodeState state, CancellationToken ct = default)
        => Task.FromResult(script(Interlocked.Increment(ref _calls)));

    public Task<AgentResponse> ProcessAsync(AgentContext ctx, CancellationToken ct = default)
        => throw new NotSupportedException("el control-loop usa RunNodeAsync");

    public event AsyncEventHandler<MessageReceivedEventArgs>?       OnMessageReceived;
    public event AsyncEventHandler<LlmCallRequestedEventArgs>?      OnLLMCallRequested;
    public event AsyncEventHandler<LlmCallRespondedEventArgs>?      OnLLMCallResponded;
    public event AsyncEventHandler<AgentResponseProducedEventArgs>? OnResponseProduced;
    public event AsyncEventHandler<AgentErrorEventArgs>?            OnError;
}

// Hoja concurrente con BARRERA: no sigue hasta que las `expected` invocaciones concurrentes
// hayan llegado. Así el solapamiento (MaxInFlight) queda DETERMINISTA — no depende de la
// programación del thread pool bajo carga — y el test demuestra concurrencia real sin flakes.
file sealed class ConcurrentLeaf(string id, string prefix, int expected) : INodeAgent
{
    int _calls, _inFlight, _maxInFlight, _arrived;
    readonly TaskCompletionSource _allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int Calls       => Volatile.Read(ref _calls);
    public int MaxInFlight => Volatile.Read(ref _maxInFlight);

    public string AgentId   => id;
    public string AgentName => id;
    public AgentRole Role   => AgentRole.Custom;

    public async Task<NodeResult> RunNodeAsync(NodeState state, CancellationToken ct = default)
    {
        var now = Interlocked.Increment(ref _inFlight);
        RaiseMax(now);
        try
        {
            Interlocked.Increment(ref _calls);
            if (Interlocked.Increment(ref _arrived) == expected)
                _allArrived.TrySetResult();                        // el último abre la barrera

            // Espera a los demás (anti-cuelgue: 10s tope). Mientras esperan, todas están en
            // vuelo a la vez → MaxInFlight == expected, garantizado.
            await _allArrived.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);

            return NodeResult.From(
                new AgentResponse { AgentId = id, AgentName = id, Role = Role },
                artifacts: [new Artifact("text", $"{prefix}:{state.Input}")]);
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }

    void RaiseMax(int value)
    {
        var seen = Volatile.Read(ref _maxInFlight);
        while (value > seen && Interlocked.CompareExchange(ref _maxInFlight, value, seen) != seen)
            seen = Volatile.Read(ref _maxInFlight);
    }

    public Task<AgentResponse> ProcessAsync(AgentContext ctx, CancellationToken ct = default)
        => throw new NotSupportedException("el control-loop usa RunNodeAsync");

    public event AsyncEventHandler<MessageReceivedEventArgs>?       OnMessageReceived;
    public event AsyncEventHandler<LlmCallRequestedEventArgs>?      OnLLMCallRequested;
    public event AsyncEventHandler<LlmCallRespondedEventArgs>?      OnLLMCallResponded;
    public event AsyncEventHandler<AgentResponseProducedEventArgs>? OnResponseProduced;
    public event AsyncEventHandler<AgentErrorEventArgs>?            OnError;
}

// Strategy del Ejemplo 2: corre el hijo y, si su signal es Failed (la condición), lo re-rutea
// (loop-back) hasta maxAttempts. Sólo primitivas del framework: IControlStrategy +
// ISignalReactiveStrategy; la History del NodeState cuenta los intentos.
file sealed class ConditionalRetryStrategy(string childId, int maxAttempts)
    : IControlStrategy, ISignalReactiveStrategy
{
    public string Name => "conditional-retry";

    public Task<ControlDecision> NextAsync(NodeState state, CancellationToken ct = default)
        => Task.FromResult(state.History.Any(h => h.Response.AgentId == childId)
            ? ControlDecision.Stop()
            : ControlDecision.Run(childId));

    public Task<ControlDecision?> OnChildSignalAsync(
        NodeState state, string child, NodeResult result, CancellationToken ct = default)
    {
        // Condición: reintentar sólo Failed (transitorio). Agotado o no transitorio → null = bubble-up.
        var attempts = state.History.Count(h => h.Response.AgentId == childId);
        return Task.FromResult<ControlDecision?>(
            result.Signal == NodeSignal.Failed && attempts < maxAttempts
                ? ControlDecision.Run(childId)
                : null);
    }
}
#pragma warning restore CS0067

// ── Los dos ejemplos de la sección "Workflows y nodos" del README ───────────────────────────
public class ReadmeExamplesTests
{
    static readonly ILogger<AgentBase<NodeResult>> Log = NullLogger<AgentBase<NodeResult>>.Instance;

    static NodeResult Ok(string id) => NodeResult.From(
        new AgentResponse { AgentId = id, AgentName = id, Role = AgentRole.Custom },
        artifacts: [new Artifact("text", $"{id}:ok")]);

    static NodeResult Fail(string id) => new()
    {
        Response = new AgentResponse
        {
            AgentId = id, AgentName = id, Role = AgentRole.Custom,
            Status = AgentStatus.Error, ErrorMessage = "transient",
        },
        Signal = NodeSignal.Failed,
    };

    // ── Ejemplo 1 — composición concurrente real: Task.WhenAll sobre WorkflowNode ──
    [Fact]
    public async Task Example1_TaskWhenAll_OverIndependentWorkflowNodes_RunsConcurrently()
    {
        var files = new[] { "a.txt", "b.txt", "c.txt" };

        // Hojas stateless compartidas entre los workflows → deben ser thread-safe.
        // expected = nº de workflows → la barrera exige que TODAS corran a la vez.
        var spell = new ConcurrentLeaf("spell", "spell", expected: files.Length);
        var sum   = new ConcurrentLeaf("summarize", "summary", expected: files.Length);

        // Instancias INDEPENDIENTES de WorkflowNode (una por archivo), misma forma.
        var jobs = files
            .Select(file => (
                Node: new WorkflowNode(
                    $"review-{file}",
                    new SequenceStrategy(["spell", "summarize"]),
                    new Dictionary<string, IAgent> { ["spell"] = spell, ["summarize"] = sum },
                    Log),
                File: file))
            .ToList();

        // Composición concurrente: Task.WhenAll sobre las instancias.
        var results = await Task.WhenAll(
            jobs.Select(j => j.Node.RunNodeAsync(new NodeState { Input = j.File })));

        results.Should().HaveCount(files.Length);
        results.Should().OnlyContain(r => r.Signal == NodeSignal.Done);
        results.Select(r => string.Join("|", r.Artifacts.Select(a => a.Name))).Should().Equal(
            "spell:a.txt|summary:a.txt",
            "spell:b.txt|summary:b.txt",
            "spell:c.txt|summary:c.txt");
        // Concurrencia REAL: las hojas llegaron a estar las 3 en vuelo a la vez.
        spell.MaxInFlight.Should().Be(files.Length);
        sum.MaxInFlight.Should().Be(files.Length);
        // Sin pérdidas: 1 llamada por hoja por workflow.
        (spell.Calls, sum.Calls).Should().Be((files.Length, files.Length));
    }

    // ── Ejemplo 2 — nodo envoltorio con reintento condicional, SIEMPRE acotado ──
    [Fact]
    public async Task Example2_RetryWrapper_RetriesTransientFailures_ThenSucceeds()
    {
        // El hijo falla 2 veces (transitorio) y a la 3ª acierta.
        var fetch = new ScriptNode("fetch", c => c <= 2 ? Fail("fetch") : Ok("fetch"));

        var wrapper = new WorkflowNode(
            "fetch-with-retry",
            new ConditionalRetryStrategy("fetch", maxAttempts: 3),
            new Dictionary<string, IAgent> { ["fetch"] = fetch },
            Log,
            new ResiliencePolicy(MaxSteps: 50));               // cota global anti-cuelgue

        var r = await wrapper.RunNodeAsync(new NodeState { Input = "go" });

        r.Signal.Should().Be(NodeSignal.Done);
        fetch.Calls.Should().Be(3);                            // 2 fallos transitorios + 1 ok
        r.Artifacts.Should().ContainSingle().Which.Name.Should().Be("fetch:ok");
    }

    [Fact]
    public async Task Example2_RetryWrapper_ExhaustsMaxAttempts_AndBubblesFailed()
    {
        // El hijo SIEMPRE falla → el wrapper agota maxAttempts y sube el último Failed.
        var fetch = new ScriptNode("fetch", _ => Fail("fetch"));

        var wrapper = new WorkflowNode(
            "fetch-with-retry",
            new ConditionalRetryStrategy("fetch", maxAttempts: 3),
            new Dictionary<string, IAgent> { ["fetch"] = fetch },
            Log,
            new ResiliencePolicy(MaxSteps: 50));

        var r = await wrapper.RunNodeAsync(new NodeState { Input = "go" });

        r.Signal.Should().Be(NodeSignal.Failed);               // acotado: no loopea infinito
        fetch.Calls.Should().Be(3);
    }

    [Fact]
    public async Task Example2_RetryWrapper_DoesNotRetryNonFailedSignals()
    {
        // La condición es "sólo Failed": un NeedsReplanning NO se reintenta, sube directo.
        var replan = new ScriptNode("replan", _ => new NodeResult
        {
            Response = new AgentResponse { AgentId = "replan", AgentName = "replan", Role = AgentRole.Custom },
            Signal   = NodeSignal.NeedsReplanning,
        });

        var wrapper = new WorkflowNode(
            "wrapper",
            new ConditionalRetryStrategy("replan", maxAttempts: 3),
            new Dictionary<string, IAgent> { ["replan"] = replan },
            Log,
            new ResiliencePolicy(MaxSteps: 50));

        var r = await wrapper.RunNodeAsync(new NodeState { Input = "go" });

        r.Signal.Should().Be(NodeSignal.NeedsReplanning);
        replan.Calls.Should().Be(1);                           // sin reintento
    }
}
