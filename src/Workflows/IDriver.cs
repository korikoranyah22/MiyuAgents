namespace MiyuAgents.Workflows;

/// <summary>
/// Quién dispara el Node raíz y responde sus <see cref="NodeSignal.NeedsInput"/>.
/// Impls previstas (§3.5 del spike): <c>HumanDriver</c> (bloquea esperando input de la UI — el
/// humano del code-tab) | <c>CharacterDriver</c> (un <c>IAgent</c> con la persona del personaje
/// que convocó → "conversa con su swarm"). El MISMO workflow corre con cualquiera de los dos
/// sin cambiar nada del Node → es lo que hace equivalentes "humano en el code-tab" y "personaje".
/// </summary>
public interface IDriver
{
    /// <summary>Responde una pregunta que subió un Node (dado el estado actual del nodo).</summary>
    Task<string> AnswerAsync(string ask, NodeState state, CancellationToken ct = default);
}
