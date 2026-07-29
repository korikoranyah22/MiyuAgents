using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MiyuAgents.Core;
using MiyuAgents.Core.Events;
using MiyuAgents.Workflows;
using Xunit;

namespace MiyuAgents.Tests.Unit.Workflows;

// ─────────────────────────────────────────────────────────────────────────────
// SPIKE 2 — rebanada headless del "PhotoWorkflow" (la forma de Anima sobre el framework, con fakes).
// Demuestra DOS mecanismos sobre un dominio real (fotos):
//   1) el DRIVER (humano O personaje) respondiendo un NeedsInput real (la escena),
//   2) el loop COMPONER → CRITICAR → REFINAR (como el critic-loop del AnimaPromptComposer),
//      con una strategy custom (extensibilidad del framework), acotado por max-rounds.
// Sin LLM, sin imagen, sin tocar el chat vivo. Los nodos reales (envolviendo AnimaPromptComposer +
// el crítico) + la imagen por BetterWaifu = rebanada siguiente.
// ─────────────────────────────────────────────────────────────────────────────

#pragma warning disable CS0067
// Compositor fake: si no tiene la escena (respuesta del driver en el historial) → pide input; si la
// tiene → compone un prompt versionado (incorporaría el feedback del crítico en el real).
file sealed class Composer(string id) : INodeAgent
{
    public string AgentId => id;
    public string AgentName => id;
    public AgentRole Role => AgentRole.Custom;

    public Task<NodeResult> RunNodeAsync(NodeState state, CancellationToken ct = default)
    {
        var scene = state.History.FirstOrDefault(h => h.Response.AgentId == "driver")?.Response.Data?.ToString();
        if (string.IsNullOrEmpty(scene))
            return Task.FromResult(NodeResult.From(Resp(""), NodeSignal.NeedsInput, ask: "¿Qué escena querés en la foto?"));

        var version = state.History.Count(h => h.Response.AgentId == id && h.Signal == NodeSignal.Done) + 1;
        var prompt  = $"[{scene}] foto v{version}";
        return Task.FromResult(NodeResult.From(Resp(prompt), NodeSignal.Done, [new Artifact("prompt", $"v{version}", prompt)]));
    }

    AgentResponse Resp(string data) => new() { AgentId = id, AgentName = id, Role = AgentRole.Custom, Data = data };
    public Task<AgentResponse> ProcessAsync(AgentContext c, CancellationToken ct = default) => throw new NotSupportedException();
    public event AsyncEventHandler<MessageReceivedEventArgs>? OnMessageReceived;
    public event AsyncEventHandler<LlmCallRequestedEventArgs>? OnLLMCallRequested;
    public event AsyncEventHandler<LlmCallRespondedEventArgs>? OnLLMCallResponded;
    public event AsyncEventHandler<AgentResponseProducedEventArgs>? OnResponseProduced;
    public event AsyncEventHandler<AgentErrorEventArgs>? OnError;
}

// Crítico fake: aprueba (Done) en la N-ésima crítica; antes pide refinar (Continue) con feedback.
file sealed class Critic(string id, int approveOnCritique) : INodeAgent
{
    public string AgentId => id;
    public string AgentName => id;
    public AgentRole Role => AgentRole.Analysis;

    public Task<NodeResult> RunNodeAsync(NodeState state, CancellationToken ct = default)
    {
        var n = state.History.Count(h => h.Response.AgentId == id) + 1;   // esta crítica es la n-ésima
        return n >= approveOnCritique
            ? Task.FromResult(NodeResult.From(Resp("aprobado"), NodeSignal.Done,     [new Artifact("critique", "pass", "aprobado")]))
            : Task.FromResult(NodeResult.From(Resp("más luz"),  NodeSignal.Continue, [new Artifact("critique", "fail", "más luz")]));
    }

    AgentResponse Resp(string data) => new() { AgentId = id, AgentName = id, Role = AgentRole.Analysis, Data = data };
    public Task<AgentResponse> ProcessAsync(AgentContext c, CancellationToken ct = default) => throw new NotSupportedException();
    public event AsyncEventHandler<MessageReceivedEventArgs>? OnMessageReceived;
    public event AsyncEventHandler<LlmCallRequestedEventArgs>? OnLLMCallRequested;
    public event AsyncEventHandler<LlmCallRespondedEventArgs>? OnLLMCallResponded;
    public event AsyncEventHandler<AgentResponseProducedEventArgs>? OnResponseProduced;
    public event AsyncEventHandler<AgentErrorEventArgs>? OnError;
}
#pragma warning restore CS0067

// Driver fake: responde la escena (podría ser un HumanDriver que bloquea en la UI, o un CharacterDriver
// que responde por LLM — misma interfaz).
file sealed class SceneDriver(string scene) : IDriver
{
    public int Asks { get; private set; }
    public Task<string> AnswerAsync(string ask, NodeState state, CancellationToken ct = default)
    { Asks++; return Task.FromResult(scene); }
}

// Strategy custom: alterna composer/critic; corta cuando el crítico aprueba o se agotan las rondas.
file sealed class ComposeCritiqueStrategy(string composerId, string criticId, int maxRounds) : IControlStrategy
{
    public string Name => "compose-critique";
    public Task<ControlDecision> NextAsync(NodeState s, CancellationToken ct = default)
    {
        if (s.History.Count(h => h.Response.AgentId == criticId) >= maxRounds)
            return D(ControlDecision.Stop());                                   // cota anti-loop

        var last = s.History.Count > 0 ? s.History[^1] : null;
        if (last is not null && last.Response.AgentId == criticId && last.Signal == NodeSignal.Done)
            return D(ControlDecision.Stop());                                   // el crítico aprobó → listo
        if (last is not null && last.Response.AgentId == composerId && last.Signal == NodeSignal.Done)
            return D(ControlDecision.Run(criticId));                            // prompt fresco → criticarlo
        return D(ControlDecision.Run(composerId));                             // arranque / driver / refinar → (re)componer
    }
    static Task<ControlDecision> D(ControlDecision d) => Task.FromResult(d);
}

public class PhotoWorkflowTests
{
    [Fact]
    public async Task PhotoWorkflow_Driver_AnswersScene_ThenComposeCritiqueLoop_UntilApproved()
    {
        var driver = new SceneDriver("una selfie en la hamaca");
        var node = new WorkflowNode(
            "photo",
            new ComposeCritiqueStrategy("composer", "critic", maxRounds: 5),
            new Dictionary<string, IAgent> { ["composer"] = new Composer("composer"), ["critic"] = new Critic("critic", approveOnCritique: 3) },
            NullLogger<AgentBase<NodeResult>>.Instance,
            driver: driver);

        var result = await node.RunNodeAsync(new NodeState { Input = "mandame una foto" });

        result.Signal.Should().Be(NodeSignal.Done);
        driver.Asks.Should().Be(1);                                            // pidió la escena una vez (NeedsInput→driver)
        result.Artifacts.Should().Contain(a => a.Kind == "critique" && a.Name == "pass");   // aprobó
        result.Artifacts.Count(a => a.Kind == "prompt").Should().Be(3);        // refinó 3 veces hasta pasar
        result.Artifacts.Where(a => a.Kind == "prompt")
            .Should().OnlyContain(a => a.Payload!.ToString()!.Contains("una selfie en la hamaca"));  // la escena del driver fluyó
    }

    [Fact]
    public async Task PhotoWorkflow_BoundedByMaxRounds_WhenCriticNeverApproves()
    {
        var node = new WorkflowNode(
            "photo",
            new ComposeCritiqueStrategy("composer", "critic", maxRounds: 3),
            new Dictionary<string, IAgent> { ["composer"] = new Composer("composer"), ["critic"] = new Critic("critic", approveOnCritique: 99) },
            NullLogger<AgentBase<NodeResult>>.Instance,
            driver: new SceneDriver("un retrato"));

        var result = await node.RunNodeAsync(new NodeState { Input = "foto" });

        result.Artifacts.Count(a => a.Kind == "critique").Should().Be(3);      // se detuvo en max-rounds (no cuelga)
        result.Artifacts.Should().NotContain(a => a.Kind == "critique" && a.Name == "pass");
    }
}
