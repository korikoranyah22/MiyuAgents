using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MiyuAgents.Core;
using MiyuAgents.Core.Events;
using MiyuAgents.Testing;
using MiyuAgents.Workflows;
using Xunit;

namespace MiyuAgents.Tests.Unit.Workflows;

#pragma warning disable CS0067
file sealed class NeedyNode(string id) : INodeAgent
{
    public int Calls { get; private set; }
    public string AgentId => id;
    public string AgentName => id;
    public AgentRole Role => AgentRole.Custom;
    public Task<NodeResult> RunNodeAsync(NodeState state, CancellationToken ct = default)
    {
        Calls++;
        return Task.FromResult(new NodeResult
        {
            Response = new AgentResponse { AgentId = id, AgentName = id, Role = AgentRole.Custom },
            Signal   = NodeSignal.NeedsInput,
            Ask      = "¿tono?",
        });
    }
    public Task<AgentResponse> ProcessAsync(AgentContext ctx, CancellationToken ct = default)
        => throw new NotSupportedException();
    public event AsyncEventHandler<MessageReceivedEventArgs>?       OnMessageReceived;
    public event AsyncEventHandler<LlmCallRequestedEventArgs>?      OnLLMCallRequested;
    public event AsyncEventHandler<LlmCallRespondedEventArgs>?      OnLLMCallResponded;
    public event AsyncEventHandler<AgentResponseProducedEventArgs>? OnResponseProduced;
    public event AsyncEventHandler<AgentErrorEventArgs>?            OnError;
}
#pragma warning restore CS0067

// ── W4: los Drivers (Human = pause/resume por UI · Character = responde por LLM) ─────────────
public class DriverTests
{
    static readonly ILogger<AgentBase<NodeResult>> Log = NullLogger<AgentBase<NodeResult>>.Instance;

    // ── HumanDriver: AnswerAsync bloquea hasta Provide ──────────────────────────
    [Fact]
    public async Task HumanDriver_BlocksUntilProvided()
    {
        var driver = new HumanDriver();
        string? asked = null;
        driver.OnAsk += (_, ask) => asked = ask;

        var answering = driver.AnswerAsync("¿tono?", new NodeState { Input = "x" });

        answering.IsCompleted.Should().BeFalse();                       // sigue abierta esperando al humano
        asked.Should().Be("¿tono?");
        var open = driver.OpenAsks.Should().ContainSingle().Subject;
        open.Ask.Should().Be("¿tono?");

        driver.Provide(open.PromptId, "oscuro").Should().BeTrue();
        (await answering).Should().Be("oscuro");
        driver.OpenAsks.Should().BeEmpty();                            // consumida
    }

    // ── HumanDriver a través de un Node: el NeedsInput se encola y el nodo reanuda al responder ──
    [Fact]
    public async Task HumanDriver_ThroughNode_ResumesOnProvide()
    {
        var driver = new HumanDriver();
        var a = new NeedyNode("a");
        var node = new WorkflowNode("root", new SequenceStrategy(["a"]),
            new Dictionary<string, IAgent> { ["a"] = a }, Log, driver: driver);

        var run = node.RunNodeAsync(new NodeState { Input = "x" });     // arranca; se bloquea en el ask

        string? pid = null;
        for (var i = 0; i < 100 && pid is null; i++)
        {
            pid = driver.OpenAsks.Count > 0 ? driver.OpenAsks[0].PromptId : null;
            if (pid is null) await Task.Delay(5);
        }
        pid.Should().NotBeNull();
        run.IsCompleted.Should().BeFalse();                            // el nodo espera al humano

        driver.Provide(pid!, "oscuro");
        (await run).Signal.Should().Be(NodeSignal.Done);               // reanudó y terminó
    }

    // ── CharacterDriver: el personaje (IAgent) responde el ask ──────────────────
    [Fact]
    public async Task CharacterDriver_AnswersViaAgent()
    {
        var kori   = ScriptedAgent.Constant("kori", "Kori", "dale, tono oscuro");
        var driver = new CharacterDriver(kori);

        var answer = await driver.AnswerAsync("¿tono?", new NodeState { Input = "x" });

        answer.Should().Be("dale, tono oscuro");
    }

    // ── CharacterDriver a través de un Node: el personaje auto-responde → el workflow avanza solo ──
    [Fact]
    public async Task CharacterDriver_ThroughNode_AutoAnswers_NoHumanNeeded()
    {
        var kori = ScriptedAgent.Constant("kori", "Kori", "oscuro");
        var a    = new NeedyNode("a");
        var node = new WorkflowNode("root", new SequenceStrategy(["a"]),
            new Dictionary<string, IAgent> { ["a"] = a }, Log, driver: new CharacterDriver(kori));

        var r = await node.RunNodeAsync(new NodeState { Input = "x" });

        r.Signal.Should().Be(NodeSignal.Done);                         // sin intervención humana
        a.Calls.Should().Be(1);
    }
}
