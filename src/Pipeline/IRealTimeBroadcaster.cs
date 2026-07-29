using MiyuAgents.Core;

namespace MiyuAgents.Pipeline;

/// <summary>
/// Un pedazo de contenido multimedia streameado al cliente durante el turno —
/// el gemelo binario de los chunks de texto de <see cref="IRealtimeBroadcaster.SendChunkAsync"/>.
/// El payload va en base64 porque el transporte típico (SignalR JSON) no lleva
/// binario crudo; <c>Seq</c> permite reordenar/dedupear en el cliente.
/// </summary>
public sealed record MediaChunk(
    string MessageId,
    AttachmentKind Kind,
    int Seq,
    string DataBase64,
    string MediaType,
    bool IsFinal);

/// <summary>
/// Channel for pushing real-time updates to connected clients.
/// The consumer implements this (e.g., wrapping SignalR IHubContext).
/// </summary>
public interface IRealtimeBroadcaster
{
    Task SendChunkAsync(string conversationId, string chunk, bool isComplete, CancellationToken ct = default);
    Task SendStatusAsync(string conversationId, string status, CancellationToken ct = default);
    Task SendErrorAsync(string conversationId, string error, CancellationToken ct = default);

    /// <summary>Push a SYSTEM message to the conversation in real time (e.g. a platform notice like "the workshop
    /// finished"). Rendered as a subtle line, not a chat bubble. Default no-op so existing impls (mocks) don't break.</summary>
    Task SendSystemMessageAsync(string conversationId, string messageId, string content, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>
    /// Push un chunk multimedia (p.ej. audio TTS generándose) a la conversación.
    /// Default no-op — mismo patrón que <see cref="SendSystemMessageAsync"/>: las
    /// impls existentes (mocks, NullBroadcaster) no se enteran.
    /// </summary>
    Task SendMediaChunkAsync(string conversationId, MediaChunk chunk, CancellationToken ct = default)
        => Task.CompletedTask;
}
