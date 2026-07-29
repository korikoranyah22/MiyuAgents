namespace MiyuAgents.Speech;

/// <summary>
/// Pedido de síntesis de voz (texto → audio).  Los campos null caen a los
/// defaults del proveedor/config del gateway.
/// </summary>
public sealed record VoiceRequest(
    string  Text,
    string? VoiceId      = null,
    string? ModelId      = null,
    string? OutputFormat = null,
    string? LanguageCode = null);

/// <summary>
/// Resultado de una síntesis.  Contrato sin excepciones de negocio (mismo
/// patrón que los clients de tools): <see cref="Success"/> discrimina; en
/// falla, <see cref="Error"/> trae la causa human-readable para el log.
/// </summary>
public sealed record VoiceSynthesisResult(
    byte[]? Audio,
    string? MediaType,
    string? Error = null)
{
    public bool Success => Audio is { Length: > 0 };
    public static VoiceSynthesisResult Fail(string error) => new(null, null, error);
}

/// <summary>Pedido de transcripción (audio → texto).  El stream lo posee el caller.</summary>
public sealed record TranscriptionRequest(
    Stream  Audio,
    string  ContentType,
    string? FileName     = null,
    string? LanguageCode = null);

/// <summary>Resultado de una transcripción.  Mismo contrato sin excepciones que la síntesis.</summary>
public sealed record TranscriptionResult(
    string? Text,
    string? LanguageCode = null,
    string? Error = null)
{
    public bool Success => !string.IsNullOrWhiteSpace(Text);
    public static TranscriptionResult Fail(string error) => new(null, null, error);
}

/// <summary>
/// Una voz disponible en la cuenta del proveedor.  <see cref="Category"/> distingue las voces PROPIAS
/// del usuario (<c>cloned</c>/<c>professional</c>/<c>generated</c>) del catálogo público (<c>premade</c>).
/// <see cref="PreviewUrl"/> es un mp3 corto de audición (sin costo de síntesis) o null si el proveedor no lo da.
/// </summary>
public sealed record VoiceInfo(
    string  VoiceId,
    string  Name,
    string? Category   = null,
    string? PreviewUrl = null);

/// <summary>Resultado de listar voces.  Mismo contrato sin excepciones que síntesis/transcripción.</summary>
public sealed record VoiceListResult(
    IReadOnlyList<VoiceInfo>? Voices,
    string? Error = null)
{
    public bool Success => Voices is not null;
    public static VoiceListResult Fail(string error) => new(null, error);
}

/// <summary>
/// Abstracción sobre un proveedor de voz (TTS + STT) — el gemelo de
/// <c>ILlmGateway</c>: el framework define el puerto, el host enchufa el
/// proveedor (ElevenLabs, etc.).  Batch-only por ahora; la variante streaming
/// (<c>SynthesizeStreamAsync</c> sobre un IAsyncEnumerable de deltas de texto)
/// entra cuando la salida de voz pase a modo en-vivo.
///
/// <para>Contrato de errores: los métodos NUNCA tiran excepciones de negocio —
/// devuelven resultados con <c>Error</c> poblado.  El caller decide retry /
/// degradar / loguear.</para>
/// </summary>
public interface IVoiceGateway
{
    /// <summary>Identificador del proveedor, p.ej. "elevenlabs".</summary>
    string ProviderName { get; }

    /// <summary>Texto → audio (batch: devuelve el binario completo).</summary>
    Task<VoiceSynthesisResult> SynthesizeAsync(VoiceRequest request, CancellationToken ct = default);

    /// <summary>Audio → texto (batch: transcripción completa del archivo).</summary>
    Task<TranscriptionResult> TranscribeAsync(TranscriptionRequest request, CancellationToken ct = default);

    /// <summary>
    /// Lista las voces de la cuenta (para poblar un selector de voz per-character).  Default no-op
    /// (lista vacía) para que los implementadores viejos / mocks no rompan al agregarse el método —
    /// mismo criterio que <c>SendMediaChunkAsync</c>.  El proveedor real lo overridea.
    /// </summary>
    Task<VoiceListResult> ListVoicesAsync(CancellationToken ct = default)
        => Task.FromResult(new VoiceListResult(Array.Empty<VoiceInfo>()));
}
