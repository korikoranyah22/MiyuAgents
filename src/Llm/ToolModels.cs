using System.Text.Json.Serialization;

namespace MiyuAgents.Llm;
public sealed record ToolDefinition(string Name, string Description, object InputSchema);
public sealed record ToolCall(string Id, string FunctionName, string ArgumentsJson);
/// <param name="Name">
/// Optional speaker name. Passed to the LLM API as the native "name" field so the
/// model can distinguish multiple speakers sharing the same role (e.g. two agents
/// both writing as "assistant"). Must be non-empty when provided.
/// </param>
/// <param name="CharacterId">
/// Optional stable id of the speaker (e.g. a character / persona id from the host
/// application).  Distinct from <paramref name="Name"/> which is the display name
/// passed to the LLM API: <c>CharacterId</c> never leaves the host — it stays as
/// metadata for the application to attribute the message to the right speaker in
/// UIs, projections, and downstream pipelines.  Backward-compat: optional default
/// null so existing callers compile unchanged.
/// </param>
/// <param name="ToolCallId">
/// Para mensajes de resultado de tool (Role="tool"): el id del <see cref="ToolCall"/> que se está
/// respondiendo (protocolo OpenAI <c>tool_call_id</c>). Necesario en el loop agéntico multi-turn.
/// </param>
/// <param name="ToolCalls">
/// Para mensajes del asistente que pidieron herramientas (Role="assistant"): las tool-calls que el
/// modelo emitió, que deben re-enviarse antes de sus resultados (protocolo OpenAI). Null = mensaje normal.
/// </param>
/// <param name="Reasoning">
/// Opcional: el razonamiento/thinking que produjo este mensaje (reasoning_content).  Metadata HOST-ONLY
/// (como <paramref name="CharacterId"/>): NUNCA se envía al LLM en el wire — queda para que el host lo
/// persista/muestre por-mensaje (reconstruir el "Razonando…" tras un reinicio).  Null = sin reasoning.
/// </param>
public sealed record ConversationMessage(
    string  Role,
    string  Content,
    string? Name        = null,
    string? CharacterId = null,
    string? ToolCallId  = null,
    IReadOnlyList<ToolCall>? ToolCalls = null,
    string? Reasoning   = null,
    // Momento del mensaje (ISO 8601 UTC). Sólo lo usa el read-path/API para que el front ordene el historial por
    // timestamp (interleaving del taller, #1). HOST-ONLY: null en el path del LLM, no se manda al modelo.
    string? Timestamp   = null,
    // Id del mensaje en la projection (para los rows user = el id del turno). HOST-ONLY como Timestamp: el front
    // lo usa para correlacionar artefactos per-turno (p.ej. notas de voz) al recargar; null en el path del LLM.
    string? MessageId   = null,
    // VISION-01 F4: ids de imágenes adjuntas del mensaje, separados por coma. HOST-ONLY como Timestamp/MessageId:
    // el front reconstruye la imagen de la burbuja tras un F5; null en el path del LLM.
    string? AttachmentIds = null,
    // Contexto interno asociado al mensaje. El host lo incorpora al wire del LLM,
    // pero la UI sigue mostrando únicamente Content.
    [property: JsonIgnore] string? HiddenContext = null);
