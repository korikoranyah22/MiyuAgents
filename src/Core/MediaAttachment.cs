namespace MiyuAgents.Core;

/// <summary>Tipo de adjunto multimedia del mensaje del usuario.</summary>
public enum AttachmentKind
{
    Image,
    Audio,
}

/// <summary>
/// Adjunto multimedia del turno, tipado.  Generaliza el par ad-hoc
/// <c>ImageBytes</c>/<c>ImageMediaType</c> que entró con visión: cada nueva
/// modalidad (audio para análisis prosódico, etc.) es un
/// <see cref="AttachmentKind"/> más, no otro par de campos nullable en
/// <see cref="AgentContext"/>.  Las propiedades <c>ImageBytes</c>/<c>ImageMediaType</c>
/// siguen existiendo como vistas computadas para los consumidores existentes.
/// </summary>
public sealed record MediaAttachment(
    AttachmentKind Kind,
    byte[] Bytes,
    string MediaType);
