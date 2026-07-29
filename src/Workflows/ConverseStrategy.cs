using MiyuAgents.Core;

namespace MiyuAgents.Workflows;

/// <summary>
/// Strategy "Converse" (§3.3/§3.6): turn-policy sobre un roster con BIDDING. Base = round-robin;
/// además <b>arbitra</b> los pedidos de turno:
/// <list type="number">
/// <item>bids REACTIVOS (un participante devolvió <see cref="NodeSignal.RequestTurn"/>) → el de mayor
///   prioridad habla — pero NO dos veces seguidas (anti-monopolio: el bid no bypassea la policy);</item>
/// <item>poll PROACTIVO opcional (si le pasaron <paramref name="pollAgents"/>): pregunta a los
///   <see cref="IBiddingParticipant"/> "¿querés hablar?";</item>
/// <item>si nadie pide → round-robin sobre el roster.</item>
/// </list>
/// Termina tras <paramref name="maxRounds"/>. (Wrappear el <c>IRoundDecisionStrategy</c> existente como
/// una policy más = adapter fino, Spike 2.)
/// </summary>
public sealed class ConverseStrategy(
    IReadOnlyList<string> roster,
    int maxRounds = 6,
    IReadOnlyDictionary<string, IAgent>? pollAgents = null) : IControlStrategy
{
    public string Name => "converse";

    public async Task<ControlDecision> NextAsync(NodeState state, CancellationToken ct = default)
    {
        if (state.Round >= maxRounds || roster.Count == 0) return ControlDecision.Stop();
        var lastSpeaker = state.History.Count > 0 ? state.History[^1].Response.AgentId : null;

        // (1) bids reactivos — el de mayor prioridad que NO acaba de hablar (anti-monopolio).
        var bid = state.Bids.Where(b => b.NodeId != lastSpeaker)
                            .OrderByDescending(b => b.Priority)
                            .FirstOrDefault();
        if (bid is not null) return ControlDecision.Run(bid.NodeId);

        // (2) poll proactivo (opcional) — el "¿querés hablar? 1/0".
        if (pollAgents is not null)
            foreach (var id in roster)
                if (id != lastSpeaker
                    && pollAgents.TryGetValue(id, out var a)
                    && a is IBiddingParticipant bp
                    && await bp.WantsTurnAsync(state, ct))
                    return ControlDecision.Run(id);

        // (3) round-robin.
        return ControlDecision.Run(roster[state.Round % roster.Count]);
    }
}
