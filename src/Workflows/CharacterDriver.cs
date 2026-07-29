using MiyuAgents.Core;

namespace MiyuAgents.Workflows;

/// <summary>
/// Driver PERSONAJE (§3.5): una INSTANCIA del personaje que convocó el workflow responde los
/// <see cref="NodeSignal.NeedsInput"/> por LLM (con su persona, vía su <see cref="IAgent"/>). Es lo
/// que hace EQUIVALENTES "humano en el code-tab" y "personaje conversando con su swarm": el MISMO
/// Node corre con <see cref="HumanDriver"/> o con este, sin cambiar nada. A diferencia del humano,
/// no bloquea — el personaje contesta al toque → el workflow avanza autónomo.
/// </summary>
public sealed class CharacterDriver(IAgent character, string conversationId = "workflow") : IDriver
{
    public async Task<string> AnswerAsync(string ask, NodeState state, CancellationToken ct = default)
    {
        var ctx  = AgentContext.For(conversationId, Guid.NewGuid().ToString("N"), ask);
        var resp = await character.ProcessAsync(ctx, ct);
        return resp.As<string>() ?? resp.Data?.ToString() ?? "";
    }
}
