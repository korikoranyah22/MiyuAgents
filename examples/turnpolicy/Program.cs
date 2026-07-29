using Microsoft.Extensions.Logging.Abstractions;
using MiyuAgents.Core;
using MiyuAgents.GroupConversations;
using MiyuAgents.Testing;
using TurnPolicyExample;

// ─────────────────────────────────────────────────────────────────────────────
// PUNTO #1 — el sueño Y la conversación grupal como POLICIES sobre UN solo rail.
//
// Hoy AngelNaira tiene DOS orquestadores: NairaGroupConversationOrchestrator
// (grupal) reimplementa el turn-taking a mano y mantiene su PROPIO _history,
// dejando el _history del framework vacío.  El sueño es OTRO loop a medida más.
//
// Este example prueba que ambos se expresan como ITurnPolicy corriendo sobre el
// MISMO DefaultGroupConversationOrchestrator, con UN historial.  Una sola llamada
// a SendMessageAsync(seed) corre el sueño entero: el loop multi-ronda del
// framework ES el loop del sueño; el maxRoundsPerTurn es sólo el techo bobo de
// seguridad, y la POLICY es el corte inteligente.
// ─────────────────────────────────────────────────────────────────────────────

var failures = new List<string>();

void Assert(bool cond, string label)
{
    Console.WriteLine($"  {(cond ? "PASS" : "FAIL")}  {label}");
    if (!cond) failures.Add(label);
}

static void PrintTranscript(GroupTurnResult r)
{
    foreach (var m in r.ProducedMessages)
        Console.WriteLine($"    · [{m.SenderName,-5}] {m.Content}");
    var cut = r.Decisions.LastOrDefault(d => d.IsEmpty);
    Console.WriteLine($"    ✂  corte: {cut?.Reason ?? "(ninguno — tope de rondas)"}");
    Console.WriteLine($"    ⤷  {r.RoundsExecuted} rondas, {r.ProducedMessages.Count} mensajes");
}

// Helpers para construir una corrida de sueño aislada.
static async Task<GroupTurnResult> RunDreamAsync(
    Func<AgentContext, int, string> charScript, int maxVueltas)
{
    var sub  = new ScriptedAgent("sub",  "Sub",
        (_, t) => $"material onírico crudo, fragmento {t}");
    var charA = new ScriptedAgent("char", "Naira", charScript);

    var orch = new DefaultGroupConversationOrchestrator(
        new DreamTurnPolicy("char", "sub", maxVueltas, cutThreshold: 0.35),
        new BroadcastRouter(),
        NullLogger<DefaultGroupConversationOrchestrator>.Instance,
        maxRoundsPerTurn: 100); // techo bobo: la policy es quien corta de verdad

    await orch.AddParticipantAsync(new HumanParticipant("human", "Usuario"), default);
    await orch.AddParticipantAsync(new AgentParticipant("char", "Naira", charA), default);
    await orch.AddParticipantAsync(new AgentParticipant("sub",  "Sub",   sub),   default);

    var seed = GroupConversationMessage.FromUser(
        "dream-conv", "human", "Usuario", "…me quedé dormida pensando en el mar.");
    return await orch.SendMessageAsync(seed, default);
}

// ── ESCENARIO 1a — sueño que corta por maxVueltas ────────────────────────────
Console.WriteLine("\n=== SUEÑO 1a · corte por maxVueltas (3) ===");
{
    string[] sueños =
    {
        "un tren parte de una estación completamente vacía",
        "la lluvia cae despacio sobre un teclado ya roto",
        "una puerta de madera se abre directamente hacia el mar",
    };
    var r = await RunDreamAsync((_, t) => sueños[Math.Min(t, sueños.Length - 1)], maxVueltas: 3);
    PrintTranscript(r);

    var charDreams = r.ProducedMessages.Count(m => m.SenderId == "char");
    var cut        = r.Decisions.LastOrDefault(d => d.IsEmpty);
    Assert(charDreams == 3, "el personaje soñó exactamente 3 veces");
    Assert(cut is not null && cut.Reason.Contains("maxVueltas"), "cortó por maxVueltas");
    Assert(r.ProducedMessages.First().SenderId == "sub", "abrió el subconsciente (bombardeo)");
}

// ── ESCENARIO 1b — sueño que corta por repetición estructural ────────────────
Console.WriteLine("\n=== SUEÑO 1b · corte por repetición estructural ===");
{
    string[] sueños =
    {
        "un tren parte de una estación completamente vacía",
        "la lluvia cae despacio sobre un teclado ya roto",
        "la lluvia cae despacio sobre un teclado ya roto", // el personaje se repite ⇒ espejo
    };
    // maxVueltas alto: queremos que gane el corte por repetición, no el de vueltas.
    var r = await RunDreamAsync((_, t) => sueños[Math.Min(t, sueños.Length - 1)], maxVueltas: 10);
    PrintTranscript(r);

    var charDreams = r.ProducedMessages.Count(m => m.SenderId == "char");
    var cut        = r.Decisions.LastOrDefault(d => d.IsEmpty);
    Assert(charDreams == 3, "el personaje soñó 3 veces antes de cortar");
    Assert(cut is not null && cut.Reason.Contains("repetición"), "cortó por repetición estructural");
}

// ── ESCENARIO 2 — conversación grupal híbrida (mención + round-robin) ─────────
Console.WriteLine("\n=== GRUPAL · mención vs round-robin (un historial) ===");
{
    var kori  = new ScriptedAgent("kori",  "Kori",  (_, t) => $"Kori responde (turno {t})");
    var naira = new ScriptedAgent("naira", "Naira", (_, t) => $"Naira responde (turno {t})");

    var orch = new DefaultGroupConversationOrchestrator(
        new HybridMentionTurnPolicy(),
        new BroadcastRouter(),
        NullLogger<DefaultGroupConversationOrchestrator>.Instance,
        maxRoundsPerTurn: 5);

    await orch.AddParticipantAsync(new HumanParticipant("human", "Usuario"), default);
    await orch.AddParticipantAsync(new AgentParticipant("kori",  "Kori",  kori),  default);
    await orch.AddParticipantAsync(new AgentParticipant("naira", "Naira", naira), default);

    async Task<GroupConversationMessage?> SayAsync(string text)
    {
        var msg = GroupConversationMessage.FromUser("grupo", "human", "Usuario", text);
        var r   = await orch.SendMessageAsync(msg, default);
        var reply = r.ProducedMessages.SingleOrDefault();
        Console.WriteLine($"    Usuario» {text}");
        Console.WriteLine($"      ↳ {(reply is null ? "(nadie respondió)" : $"[{reply.SenderName}] {reply.Content}")}");
        Assert(r.ProducedMessages.Count == 1, $"exactamente un agente respondió a «{text}»");
        return reply;
    }

    var r1 = await SayAsync("hola Kori, ¿cómo andás?"); // mención explícita
    var r2 = await SayAsync("¿y vos qué opinás?");        // sin mención ⇒ round-robin
    var r3 = await SayAsync("dale, buenísimo");           // sin mención ⇒ round-robin

    Assert(r1?.SenderId == "kori",  "mención a Kori ⇒ responde Kori");
    Assert(r2?.SenderId == "naira", "sin mención ⇒ round-robin pasa a Naira");
    Assert(r3?.SenderId == "kori",  "sin mención otra vez ⇒ round-robin vuelve a Kori");
    // El historial es UNO solo y compartido — lo confirma que el round-robin
    // recuerde quién habló al final del turno anterior.
    Assert(orch.History.Count(m => m.SenderKind == ParticipantKind.Agent) == 3,
        "el rail mantuvo UN historial con las 3 respuestas");
}

// ── Veredicto ────────────────────────────────────────────────────────────────
Console.WriteLine();
if (failures.Count == 0)
{
    Console.WriteLine("✅ TODO VERDE — sueño y grupal corren como policies sobre un solo rail.");
    return 0;
}

Console.WriteLine($"❌ {failures.Count} fallo(s):");
foreach (var f in failures) Console.WriteLine($"   - {f}");
return 1;
