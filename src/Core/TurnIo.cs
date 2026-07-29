namespace MiyuAgents.Core;

/// <summary>Modalidad de un lado del turno (entrada del usuario o salida del agente).</summary>
public enum IoModality
{
    Text,
    Voice,
}

/// <summary>
/// Modalidad de I/O del turno: cómo ENTRÓ el mensaje del usuario y cómo debe SALIR
/// la respuesta del agente.  Es información del turno (dos mensajes seguidos pueden
/// ir en modos distintos), tipada en el context por la misma razón que
/// <see cref="ConversationMode"/>: preferir esto a leer claves sueltas de
/// <c>Metadata</c>.
///
/// <para>La modalidad es TRANSPORTE/presentación: el pipeline siempre ve texto
/// (la voz entrante llega ya transcripta; la voz saliente se sintetiza del texto
/// generado).  Ningún stage de memoria/emoción/contexto debería cambiar su
/// comportamiento por esto — solo las capas de voz (síntesis post-turno, relay
/// de streaming) lo consultan.</para>
/// </summary>
public sealed record TurnIo(IoModality Input, IoModality Output)
{
    /// <summary>Default: el chat de texto de siempre.</summary>
    public static readonly TurnIo Text = new(IoModality.Text, IoModality.Text);

    /// <summary>True si la respuesta del agente debe salir también como audio.</summary>
    public bool WantsVoiceOutput => Output == IoModality.Voice;

    /// <summary>True si el mensaje del usuario entró por micrófono (ya transcripto).</summary>
    public bool CameFromVoice => Input == IoModality.Voice;

    /// <summary>
    /// Parsea el formato de wire <c>"{input}-{output}"</c> con tokens
    /// <c>text</c>/<c>voice</c> (p.ej. <c>"voice-text"</c>).  Null, vacío o
    /// malformado → <see cref="Text"/> (backward-compatible: clientes viejos
    /// no mandan el campo y todo sigue siendo texto).
    /// </summary>
    public static TurnIo Parse(string? wire)
    {
        if (string.IsNullOrWhiteSpace(wire)) return Text;
        var parts = wire.Split('-', 2);
        if (parts.Length != 2) return Text;
        return new TurnIo(ParseSide(parts[0]), ParseSide(parts[1]));
    }

    private static IoModality ParseSide(string token) =>
        string.Equals(token.Trim(), "voice", StringComparison.OrdinalIgnoreCase)
            ? IoModality.Voice
            : IoModality.Text;
}
