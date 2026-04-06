using System.Text.Json;
using Memoria.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using MiyuAgents.Core;
using MiyuAgents.Llm;

namespace Memoria.Agents;

/// <summary>
/// Analyses each user message with the LLM and extracts durable, explicit facts
/// (name, age, pets, schedule, preferences…) that are worth remembering long-term.
///
/// Extracted facts are written to ctx.Results.Extra["newFacts"] (List&lt;ExtractedFact&gt;)
/// so Program.cs can embed and store them via FactMemoryAgent.StoreFactAsync.
/// </summary>
public sealed class FactExtractorAgent(string model, ILlmGateway gateway)
    : AgentBase<List<ExtractedFact>>(NullLogger<AgentBase<List<ExtractedFact>>>.Instance)
{
    private const string ExtractionKey = "newFacts";

    public override string    AgentId   => "agent-fact-extractor";
    public override string    AgentName => "FactExtractor";
    public override AgentRole Role      => AgentRole.Analysis;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    protected override async Task<List<ExtractedFact>?> ExecuteCoreAsync(
        AgentContext ctx, CancellationToken ct)
    {
        var systemPrompt =
            "Eres un extractor de memoria para una IA compañera. Analiza el mensaje del usuario " +
            "y extrae SOLO los hechos EXPLÍCITOS y DURABLES que una IA debería recordar a largo " +
            "plazo para conocer mejor a esta persona.\n\n" +
            "Extrae cualquier dato relevante: nombre, edad, ubicación, trabajo, horarios, familia, " +
            "mascotas, relaciones, gustos, hobbies, hábitos, comidas, bebidas, miedos, sueños, " +
            "logros, rutinas, o cualquier otro dato concreto sobre su vida.\n\n" +
            "Reglas:\n" +
            "- Solo hechos EXPLÍCITOS. No inferencias ni suposiciones.\n" +
            "- Solo hechos DURABLES (no estados transitorios como 'está enojado ahora').\n" +
            "- Claves en español, descriptivas y en minúsculas (ej: 'mascotas', 'horario_trabajo').\n" +
            "- Si no hay hechos durables, devuelve [].\n" +
            "- Responde SOLO con JSON válido, sin texto adicional.\n\n" +
            "Formato: [{\"clave\": \"nombre_clave\", \"valor\": \"valor del hecho\"}]";

        var request = new LlmRequest
        {
            Model        = model,
            SystemPrompt = systemPrompt,
            Messages     = [new ConversationMessage("user", ctx.UserMessage)],
            MaxTokens    = 300,
            Temperature  = 0f   // deterministic extraction
        };

        var response = await gateway.CompleteAsync(request, ct);

        if (string.IsNullOrWhiteSpace(response.Content))
            return [];

        var facts = ParseFacts(response.Content);
        if (facts.Count > 0)
            ctx.Results.Extra[ExtractionKey] = facts;

        return facts;
    }

    private static List<ExtractedFact> ParseFacts(string json)
    {
        try
        {
            // Strip any markdown fences the model might have added.
            var trimmed = json.Trim();
            var start   = trimmed.IndexOf('[');
            var end     = trimmed.LastIndexOf(']');
            if (start < 0 || end <= start) return [];

            var array = trimmed[start..(end + 1)];
            var items = JsonSerializer.Deserialize<List<ExtractedFactJson>>(array, _jsonOptions);
            if (items is null) return [];

            return items
                .Where(i => !string.IsNullOrWhiteSpace(i.Clave) && !string.IsNullOrWhiteSpace(i.Valor))
                .Select(i => new ExtractedFact(i.Clave!.Trim(), i.Valor!.Trim()))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    // Intermediate deserialization type (clave/valor from LLM JSON).
    private sealed class ExtractedFactJson
    {
        public string? Clave { get; set; }
        public string? Valor { get; set; }
    }
}
