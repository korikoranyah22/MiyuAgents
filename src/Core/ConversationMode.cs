namespace MiyuAgents.Core;

/// <summary>
/// Modo de la conversación que produce este turno.  Reemplaza los "magic string"
/// sueltos en <see cref="AgentContext.Metadata"/> (p.ej. <c>Metadata["dream"]</c>)
/// por un campo tipado que pipelines, turn-policies y enrutado de modelos pueden
/// leer sin acordarse de la clave correcta.
///
/// Es un <see cref="FlagsAttribute"/> porque los modos componen: un sueño húmedo
/// es <c>Dream | Carnal</c>; una grupal normal es <c>Group</c>.  El default
/// <see cref="Normal"/> (= 0) es el 1:1 de toda la vida, así que un contexto que
/// no setea nada se comporta idéntico al previo.
/// </summary>
[Flags]
public enum ConversationMode
{
    /// <summary>1:1 humano↔personaje. El default.</summary>
    Normal = 0,

    /// <summary>Conversación grupal (varios participantes / turn-policy).</summary>
    Group = 1 << 0,

    /// <summary>Turno onírico: el loop del sueño, sin humano en vivo.</summary>
    Dream = 1 << 1,

    /// <summary>Sesgo carnal/íntimo. Ortogonal: combina con <see cref="Dream"/> (sueño húmedo).</summary>
    Carnal = 1 << 2,

    /// <summary>Turno de AVISO DEL SISTEMA (item #7): NO lo mandó el humano — lo dispara la plataforma (p.ej. "el
    /// taller terminó"). El personaje REACCIONA, pero el disparador es una nota de sistema (se muestra como línea,
    /// no como globo de usuario) y NO se analiza como emoción del usuario (no la hubo). Sin humano en vivo, como
    /// <see cref="Dream"/>, pero con globo de respuesta del personaje.</summary>
    System = 1 << 3,
}
