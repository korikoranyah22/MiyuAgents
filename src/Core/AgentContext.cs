using MiyuAgents.Llm;

namespace MiyuAgents.Core;

/// <summary>
/// Immutable turn context. All identity fields are init-only.
/// Agents write results into ctx.Results (the mutable accumulator).
/// Agents must NOT create a new context — they receive and return the same instance.
/// </summary>
public sealed record AgentContext
{
    // ── Turn identity (immutable) ────────────────────────────────────────────
    public required string ConversationId { get; init; }
    public required string MessageId      { get; init; }
    public required string ProfileId      { get; init; }
    public required string CharacterId    { get; init; }

    /// <summary>
    /// Conversaciones grupales: set de perfiles de TODOS los participantes
    /// (humano + mirrors de los otros chars), EXCLUYENDO el mirror del propio
    /// respondedor.  El personaje (<see cref="CharacterId"/>) es fijo; los
    /// pasos de retrieval (memoria, facts, emocional) recorren
    /// <c>CharacterId × {cada ParticipantProfileId}</c> para que el char
    /// reciba SUS propias memorias/facts con todos los interlocutores de esta
    /// conversación.  Vacío en 1:1 → se usa el <see cref="ProfileId"/> singular
    /// (comportamiento idéntico al previo).  <see cref="ProfileId"/> sigue
    /// siendo el interlocutor de ESTE turno (storage + atribución + framing).
    /// </summary>
    public IReadOnlyList<string> ParticipantProfileIds { get; init; } = [];

    // ── User input (immutable) ───────────────────────────────────────────────
    public required string UserMessage    { get; init; }

    /// <summary>
    /// When the message was fragmented from a long input, this is the full original text.
    /// Useful for memory retrieval agents that need the full semantic content.
    /// Null for normal messages.
    /// </summary>
    public string? OriginalFullMessage    { get; init; }

    /// <summary>
    /// Adjuntos multimedia del mensaje del usuario, tipados por
    /// <see cref="AttachmentKind"/>.  Vacío cuando el mensaje es solo texto.
    /// Generaliza el par ImageBytes/ImageMediaType original (que quedan abajo
    /// como vistas computadas de compatibilidad).
    /// </summary>
    public IReadOnlyList<MediaAttachment> Attachments { get; init; } = [];

    /// <summary>Todas las imágenes adjuntas, en el orden enviado por el usuario.</summary>
    public IReadOnlyList<MediaAttachment> ImageAttachments =>
        Attachments.Where(a => a.Kind == AttachmentKind.Image).ToArray();

    /// <summary>
    /// Compat: bytes de la primera imagen adjunta (vista computada sobre
    /// <see cref="Attachments"/>).  Null cuando no hay imagen.
    /// </summary>
    public byte[]? ImageBytes =>
        Attachments.FirstOrDefault(a => a.Kind == AttachmentKind.Image)?.Bytes;

    /// <summary>Compat: media type de la primera imagen adjunta.</summary>
    public string? ImageMediaType =>
        Attachments.FirstOrDefault(a => a.Kind == AttachmentKind.Image)?.MediaType;

    // ── Conversation state (immutable snapshot) ──────────────────────────────
    public required IReadOnlyList<ConversationMessage> History { get; init; }
    public required bool IsFirstTurn { get; init; }

    // ── Runtime configuration (immutable) ────────────────────────────────────
    public required string Model          { get; init; }

    /// <summary>
    /// Modo tipado del turno (1:1 / grupal / sueño / carnal, componibles).
    /// Default <see cref="ConversationMode.Normal"/> → comportamiento 1:1 idéntico
    /// al previo.  Preferir esto a leer claves sueltas de <see cref="Metadata"/>.
    /// </summary>
    public ConversationMode Mode { get; init; } = ConversationMode.Normal;

    /// <summary>
    /// Modalidad de I/O del turno (texto/voz × texto/voz).  Default
    /// <see cref="TurnIo.Text"/> → chat de texto idéntico al previo.  Solo la
    /// consultan las capas de voz (síntesis/relay); el pipeline ve texto siempre.
    /// </summary>
    public TurnIo Io { get; init; } = TurnIo.Text;

    /// <summary>True si el turno es parte de una conversación grupal.</summary>
    public bool IsGroup  => Mode.HasFlag(ConversationMode.Group);
    /// <summary>True si el turno es onírico (loop del sueño, sin humano en vivo).</summary>
    public bool IsDream  => Mode.HasFlag(ConversationMode.Dream);
    /// <summary>True si el turno lleva sesgo carnal/íntimo (p.ej. sueño húmedo).</summary>
    public bool IsCarnal => Mode.HasFlag(ConversationMode.Carnal);
    /// <summary>True si el turno es un AVISO DEL SISTEMA (#7): lo disparó la plataforma, no el humano (p.ej. "el
    /// taller terminó"). El personaje reacciona, pero el disparador no es emoción del usuario.</summary>
    public bool IsSystem => Mode.HasFlag(ConversationMode.System);

    public IDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();

    // ── Mutable accumulator (agents write here) ──────────────────────────────
    public AgentContextAccumulator Results { get; init; } = new();

    // ── Factory ──────────────────────────────────────────────────────────────
    public static AgentContext For(
        string conversationId, string messageId, string userMessage,
        string model = "default") =>
        new()
        {
            ConversationId = conversationId,
            MessageId      = messageId,
            ProfileId      = "",
            CharacterId    = "",
            UserMessage    = userMessage,
            History        = [],
            IsFirstTurn    = true,
            Model          = model
        };
}
