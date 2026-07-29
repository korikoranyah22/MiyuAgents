using MiyuAgents.Core;

namespace MiyuAgents.Workflows;

/// <summary>
/// Converse con FAIRNESS + RELEVANCIA (idea de Miyu para grupos grandes). A diferencia de
/// <see cref="ConverseStrategy"/> (round-robin puro), acá la selección combina cuatro palancas, todas
/// configurables (defaults = equidad pura, sin poll):
///
/// <list type="bullet">
/// <item><b>Equidad:</b> la base NO es round-robin sino el que MENOS participó (desempate ALEATORIO),
///   excluyendo al último (anti-ping-pong).</item>
/// <item><b>request-turn (bids):</b> se honran pero ACOTADOS por participación — un char que ya domina no
///   puede bidear más mientras otros están afuera (<paramref name="bidFairnessSlack"/>). Rompe los loops de
///   2-3 que dejan al resto sin hablar.</item>
/// <item><b>Recencia (<paramref name="recencyWindow"/>):</b> la participación se cuenta sólo en los últimos
///   N turnos (null = toda la historia). En charlas largas evita que la equidad se "duerma".</item>
/// <item><b>Cuota por persona (<paramref name="weights"/>):</b> participación EFECTIVA = count / peso. Un
///   char hablador (peso 2) tolera el doble de turnos que uno callado (peso 1) antes de "estar al día" — la
///   equidad respeta la personalidad sin permitir monopolio.</item>
/// <item><b>Relevancia (<paramref name="pollAgents"/>):</b> si se pasan, sólo hablan los que QUIEREN hablar
///   (<see cref="IBiddingParticipant.WantsTurnAsync"/>); entre los que quieren, la equidad elige. Si NADIE
///   quiere → <see cref="ControlDecision.Stop"/> (la conversación se asentó → cede al humano, en vez de
///   seguir con round-robin vacío). Se pollea en orden de menos-participación y se corta en el primero que
///   quiere → costo acotado.</item>
/// </list>
/// Termina tras <paramref name="maxRounds"/>.
/// </summary>
public sealed class FairConverseStrategy(
    IReadOnlyList<string> roster,
    int maxRounds = 12,
    double bidFairnessSlack = 1,
    Random? rng = null,
    int? recencyWindow = null,
    IReadOnlyDictionary<string, double>? weights = null,
    IReadOnlyDictionary<string, IAgent>? pollAgents = null,
    string? openingSpeaker = null,   // MENCIÓN: el humano dirigió el mensaje a este char → abre el turno
    Action<string, string>? onDecision = null) : IControlStrategy   // observabilidad: (idElegido, motivo) por turno
{
    /// <summary>Envuelve el Run notificando el MOTIVO (para el chip "por qué habló X"). Domain-neutral.</summary>
    ControlDecision Pick(string id, string reason)
    {
        onDecision?.Invoke(id, reason);
        return ControlDecision.Run(id);
    }
    static string PollReason(string? last) =>
        last is null || string.Equals(last, "driver", StringComparison.OrdinalIgnoreCase) ? "te responde" : "quiso sumar";

    readonly Random _rng = rng ?? Random.Shared;

    public string Name => "fair-converse";

    public async Task<ControlDecision> NextAsync(NodeState state, CancellationToken ct = default)
    {
        if (state.Round >= maxRounds || roster.Count == 0)
            return ControlDecision.Stop();

        // MENCIÓN: si el humano dirigió el mensaje a un personaje, ESE abre el turno (override de la equidad),
        // sólo en la APERTURA del run (Round 0). Después, flujo normal (equidad + bids + poll). Lo pull-ea aunque
        // estuviera callado; se corta acá si el id mencionado no está en el roster.
        if (state.Round == 0 && openingSpeaker is not null)
        {
            var opId = roster.FirstOrDefault(r => string.Equals(r, openingSpeaker, StringComparison.OrdinalIgnoreCase));
            if (opId is not null) return Pick(opId, "mención");
        }

        var lastSpeaker = state.History.Count > 0 ? state.History[^1].Response.AgentId : null;

        // Participación EFECTIVA (count/peso) en la ventana de recencia.
        var window = recencyWindow is { } w and > 0
            ? state.History.TakeLast(w)
            : state.History;
        var counts = roster.ToDictionary(id => id, _ => 0, StringComparer.OrdinalIgnoreCase);
        foreach (var h in window)
            if (h.Response.AgentId is { } id && counts.ContainsKey(id))
                counts[id]++;
        double Eff(string id) => counts[id] / Weight(id);
        var minEff = roster.Min(Eff);

        // (1) Bids (request-turn) ACOTADOS por equidad: no el que recién habló, y que no esté dominando.
        var eligibleBids = state.Bids
            .Select(b => b.NodeId)
            .Where(id => counts.ContainsKey(id)
                      && !string.Equals(id, lastSpeaker, StringComparison.OrdinalIgnoreCase)
                      && Eff(id) <= minEff + bidFairnessSlack)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (PickLeast(eligibleBids, Eff) is { } bidPick)
            return Pick(bidPick, "pidió turno");

        // Candidatos base: el roster sin el último (anti-ping-pong), ordenados por menos participación
        // efectiva, con desempate aleatorio.
        var candidates = roster
            .Where(id => !string.Equals(id, lastSpeaker, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (candidates.Count == 0) candidates = roster.ToList();   // grupo de 1 → no hay otro
        var ordered = candidates
            .OrderBy(Eff)
            .ThenBy(_ => _rng.Next())
            .ToList();

        // (2) Con POLL de relevancia: se pollea en orden de equidad y se toma el primero que QUIERE hablar
        //     (= el de menos participación entre los que quieren). Si nadie quiere → Stop (se asentó).
        if (pollAgents is not null)
        {
            foreach (var id in ordered)
            {
                if (pollAgents.TryGetValue(id, out var a) && a is IBiddingParticipant bp)
                {
                    if (await bp.WantsTurnAsync(state, ct))
                        return Pick(id, PollReason(lastSpeaker));
                }
                else
                {
                    // Sin participante polleador para este id → no podemos preguntar: lo asumimos dispuesto.
                    return Pick(id, PollReason(lastSpeaker));
                }
            }
            return ControlDecision.Stop();   // nadie quiere el piso → cede al humano
        }

        // (3) Sin poll: el de menos participación efectiva (desempate aleatorio ya aplicado).
        return Pick(ordered[0], "equidad");
    }

    double Weight(string id) =>
        weights is not null && weights.TryGetValue(id, out var w) && w > 0 ? w : 1.0;

    /// <summary>De los <paramref name="ids"/>, el de menor participación efectiva; desempate ALEATORIO.</summary>
    string? PickLeast(IReadOnlyList<string> ids, Func<string, double> eff)
    {
        if (ids.Count == 0) return null;
        var least = ids.Min(eff);
        var tied = ids.Where(id => Math.Abs(eff(id) - least) < 1e-9).ToList();
        return tied[_rng.Next(tied.Count)];
    }
}
