using Microsoft.Extensions.Logging.Abstractions;
using MiyuAgents.Core;

namespace MiyuAgents.Testing;

/// <summary>
/// Agente determinista sin LLM, guionado por número de turno.  Sirve para validar
/// la MECÁNICA de orquestación (quién habla y cuándo, cortes de turn-policy,
/// ciclo de vida de sesiones) aislada de cualquier modelo: el "cerebro" del turno
/// vive en la <see cref="MiyuAgents.GroupConversations.ITurnPolicy"/> / en el
/// orquestador, no en el agente.
///
/// El script recibe el <see cref="AgentContext"/> y el índice de invocación de
/// ESTE agente (0-based, propio de cada instancia) y devuelve el texto a producir
/// (o <c>null</c> para una respuesta vacía).  Es el double compartido por el
/// ejemplo <c>turnpolicy</c> y las suites de tests.
/// </summary>
public sealed class ScriptedAgent : AgentBase<string>
{
    private readonly Func<AgentContext, int, string?> _script;
    private int _turn;

    public ScriptedAgent(
        string id,
        string name,
        Func<AgentContext, int, string?> script,
        AgentRole role = AgentRole.Conversation)
        : base(NullLogger<AgentBase<string>>.Instance)
    {
        AgentId   = id;
        AgentName = name;
        Role      = role;
        _script   = script;
    }

    public override string    AgentId   { get; }
    public override string    AgentName { get; }
    public override AgentRole Role      { get; }

    /// <summary>Cuántas veces se invocó este agente (útil para asserts de orden/llamadas).</summary>
    public int TurnCount => Volatile.Read(ref _turn);

    protected override Task<string?> ExecuteCoreAsync(AgentContext ctx, CancellationToken ct)
        => Task.FromResult(_script(ctx, _turn++));

    // ── Factories de conveniencia ─────────────────────────────────────────────

    /// <summary>Responde siempre el mismo texto, ignorando el turno.</summary>
    public static ScriptedAgent Constant(string id, string name, string reply)
        => new(id, name, (_, _) => reply);

    /// <summary>
    /// Responde la línea del array según el turno; en turnos más allá del array,
    /// repite la última (útil para forzar cortes por repetición estructural).
    /// </summary>
    public static ScriptedAgent FromTurns(string id, string name, params string[] turns)
        => new(id, name, (_, t) => turns.Length == 0 ? null : turns[Math.Min(t, turns.Length - 1)]);
}
