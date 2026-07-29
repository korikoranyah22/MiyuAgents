using MiyuAgents.Core;
using MiyuAgents.GroupConversations;

namespace TurnPolicyExample;

/// <summary>
/// EL SUEÑO COMO POLICY.  Toda la lógica de turnos del loop onírico —
/// alternancia fija humano→sub→char→sub→char… y los DOS cortes (maxVueltas +
/// repetición estructural) — vive acá, no en un orquestador a medida.  El rail
/// del framework (<c>DefaultGroupConversationOrchestrator</c>) corre el loop y
/// mantiene UN solo historial; esta policy es el cerebro de "quién sigue / cuándo
/// cortar".  Una sola llamada a <c>SendMessageAsync(seed)</c> corre el sueño
/// entero, porque el loop multi-ronda del framework ES el loop del sueño.
/// </summary>
public sealed class DreamTurnPolicy(
    string charId, string subId, int maxVueltas, double cutThreshold = 0.35) : ITurnPolicy
{
    public string PolicyName => "dream";

    public Task<TurnSelection> SelectRespondersAsync(
        GroupConversationMessage message,
        IReadOnlyList<IParticipant> participants,
        IReadOnlyList<GroupConversationMessage> history,
        GroupConversationContext ctx,
        CancellationToken ct)
    {
        var agents = participants.OfType<AgentParticipant>().ToList();
        var charP  = agents.FirstOrDefault(a => a.ParticipantId == charId);
        var subP   = agents.FirstOrDefault(a => a.ParticipantId == subId);
        if (charP is null || subP is null)
            return Done(TurnSelection.None("falta el personaje o el subconsciente"));

        var last = history.LastOrDefault();
        if (last is null)
            return Done(TurnSelection.None("sin seed — nada que soñar"));

        // ── CORTE 1: maxVueltas (contamos los sueños del personaje) ──────────
        var vueltas = history.Count(m => m.SenderId == charId);
        if (vueltas >= maxVueltas)
            return Done(TurnSelection.None($"maxVueltas ({maxVueltas}) alcanzado"));

        // ── Alternancia FIJA ────────────────────────────────────────────────
        // humano(seed) → sub(bombardeo) → char(sueña) → sub(escarba) → char → …
        if (last.SenderKind == ParticipantKind.Human)
            return Done(new TurnSelection([subP], "seed → bombardeo del subconsciente"));

        if (last.SenderId == subId)
            return Done(new TurnSelection([charP], "el personaje sueña"));

        // last == char → ¿CORTE 2 (repetición estructural) o escarbado?
        var prevDreams = history.Where(m => m.SenderId == charId)
                                .Select(m => m.Content)
                                .Reverse().Skip(1).ToList();
        var overlap = prevDreams.Count > 0 ? TextOverlap.MaxNGramOverlap(last.Content, prevDreams) : 0;
        if (overlap > cutThreshold)
            return Done(TurnSelection.None($"repetición estructural (overlap {overlap:0.00})"));

        return Done(new TurnSelection([subP], "el subconsciente escarba"));
    }

    private static Task<TurnSelection> Done(TurnSelection s) => Task.FromResult(s);
}

/// <summary>
/// CONVERSACIÓN GRUPAL HÍBRIDA COMO POLICY.  Un agente por mensaje humano:
/// si el humano menciona a un agente por nombre, responde ése; si no, round-robin
/// (el siguiente al último que habló).  El mismo rail que el sueño, otra policy.
/// </summary>
public sealed class HybridMentionTurnPolicy : ITurnPolicy
{
    public string PolicyName => "hybrid-mention";

    public Task<TurnSelection> SelectRespondersAsync(
        GroupConversationMessage message,
        IReadOnlyList<IParticipant> participants,
        IReadOnlyList<GroupConversationMessage> history,
        GroupConversationContext ctx,
        CancellationToken ct)
    {
        var agents = participants.OfType<AgentParticipant>().ToList();
        if (agents.Count == 0) return Done(TurnSelection.None("sin agentes"));

        // Un solo agente por turno humano: si ya respondió alguien DESPUÉS del
        // último mensaje humano, cortamos la ronda (None).
        var lastHumanIdx = LastIndexWhere(history, m => m.SenderKind == ParticipantKind.Human);
        var respondedThisTurn = history.Skip(lastHumanIdx + 1)
                                       .Any(m => m.SenderKind == ParticipantKind.Agent);
        if (respondedThisTurn)
            return Done(TurnSelection.None("ya respondió un agente este turno"));

        var humanMsg = lastHumanIdx >= 0 ? history[lastHumanIdx].Content : message.Content;

        // Mención explícita por nombre → ese agente.
        var mentioned = agents.FirstOrDefault(a =>
            humanMsg.Contains(a.DisplayName, StringComparison.OrdinalIgnoreCase));
        if (mentioned is not null)
            return Done(new TurnSelection([mentioned], $"mención a {mentioned.DisplayName}"));

        // Sin mención → round-robin: el siguiente al último agente que habló.
        var lastAgentId = history.LastOrDefault(m => m.SenderKind == ParticipantKind.Agent)?.SenderId;
        var idx  = lastAgentId is null ? -1 : agents.FindIndex(a => a.ParticipantId == lastAgentId);
        var next = agents[(idx + 1) % agents.Count];
        return Done(new TurnSelection([next], $"round-robin → {next.DisplayName}"));
    }

    private static int LastIndexWhere<T>(IReadOnlyList<T> list, Func<T, bool> pred)
    {
        for (var i = list.Count - 1; i >= 0; i--) if (pred(list[i])) return i;
        return -1;
    }

    private static Task<TurnSelection> Done(TurnSelection s) => Task.FromResult(s);
}
