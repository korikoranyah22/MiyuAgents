using MiyuAgents.Core;

namespace MiyuAgents.Workflows;

/// <summary>
/// Capability OPCIONAL (§3.6 del spike): un participante que puede ser SONDEADO "¿querés hablar?"
/// — el poll PROACTIVO del bidding (el "1/0 con max_tokens=1" en la impl real). La
/// <see cref="ConverseStrategy"/> lo consulta si le pasaron los agentes. Un agente que NO la
/// implementa simplemente no se sondea (sólo puede bidear REACTIVO, devolviendo
/// <see cref="NodeSignal.RequestTurn"/> tras hablar).
/// </summary>
public interface IBiddingParticipant : IAgent
{
    /// <summary>¿Este participante quiere el próximo turno, dado el estado actual? (poll barato).</summary>
    Task<bool> WantsTurnAsync(NodeState state, CancellationToken ct = default);
}
