using Memoria.Agents;
using Memoria.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using MiyuAgents.Core;
using MiyuAgents.Pipeline;

namespace Memoria.Pipeline;

/// <summary>
/// Stage 1 (priority 10): calls FactExtractorAgent to find durable facts in the
/// user message, then persists each one to Qdrant via FactMemoryAgent.StoreFactAsync.
/// Fire-and-forget — returns immediately and does not block the pipeline.
/// onFactStored is called for each fact after it is persisted (e.g. to update session state).
/// </summary>
public sealed class FactExtractionStage(
    FactExtractorAgent    extractor,
    FactMemoryAgent       factAgent,
    Action<ExtractedFact>? onFactStored = null) : IPipelineStage
{
    public string StageName => "FactExtraction";
    public int    Priority  => 10;

    public Task<PipelineStageResult> ExecuteAsync(
        AgentContext ctx, PipelineContext pipeline, CancellationToken ct)
    {
        // Fire-and-forget: extraction stores facts for the NEXT turn.
        // The current turn does not wait for this to finish.
        _ = RunExtractionAsync(ctx, ct);
        return Task.FromResult(PipelineStageResult.Continue(StageName));
    }

    private async Task RunExtractionAsync(AgentContext ctx, CancellationToken ct)
    {
        try
        {
            await extractor.ProcessAsync(ctx, ct);

            if (ctx.Results.Extra.TryGetValue("newFacts", out var nf)
                && nf is List<ExtractedFact> facts && facts.Count > 0)
            {
                Console.WriteLine($"\n── FactExtractor: {facts.Count} fact(s) extraído(s) ──────────────");
                foreach (var f in facts)
                {
                    Console.WriteLine($"   {f.Clave}: {f.Valor}");
                    await factAgent.StoreFactAsync(f.Clave, f.Valor, ct);
                    onFactStored?.Invoke(f);
                }
                Console.WriteLine("──────────────────────────────────────────────────────────────\n");
            }
            else
            {
                Console.WriteLine("\n── FactExtractor: sin hechos durables en este mensaje ─────────\n");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n── FactExtractor: error — {ex.Message} ─────────────────────────\n");
        }
    }
}

/// <summary>
/// Stage 2 (priority 20): calls FactMemoryAgent to embed the user message and
/// retrieve semantically relevant facts, writing them to ctx.Results.Facts.
/// Best-effort — failure does not abort the pipeline.
/// </summary>
public sealed class FactRetrievalStage(FactMemoryAgent factAgent) : IPipelineStage
{
    public string StageName => "FactRetrieval";
    public int    Priority  => 20;

    public async Task<PipelineStageResult> ExecuteAsync(
        AgentContext ctx, PipelineContext pipeline, CancellationToken ct)
    {
        try
        {
            await factAgent.ProcessAsync(ctx, ct);

            var facts = ctx.Results.Facts.OfType<FactHit>().ToList();
            if (facts.Count > 0)
            {
                Console.WriteLine($"\n── FactRetrieval: {facts.Count} recuerdo(s) inyectado(s) ─────────");
                foreach (var f in facts)
                    Console.WriteLine($"   [{f.Score:F2}] {f.Text}");
                Console.WriteLine("──────────────────────────────────────────────────────────────\n");
            }
            else
            {
                Console.WriteLine("\n── FactRetrieval: sin recuerdos relevantes ────────────────────\n");
            }
        }
        catch { /* best-effort — chat continues without retrieved facts */ }

        return PipelineStageResult.Continue(StageName);
    }
}

/// <summary>
/// Stage 3 (priority 30): injects the pipeline broadcaster into ctx.Metadata so
/// MemoryAgent can stream tokens, then runs the conversational agent.
/// </summary>
public sealed class ChatStage(MemoryAgent agent) : IPipelineStage
{
    public string StageName => "Chat";
    public int    Priority  => 30;

    public async Task<PipelineStageResult> ExecuteAsync(
        AgentContext ctx, PipelineContext pipeline, CancellationToken ct)
    {
        ctx.Metadata["broadcaster"] = pipeline.Broadcaster;
        await agent.ProcessAsync(ctx, ct);
        return PipelineStageResult.Continue(StageName);
    }
}

/// <summary>
/// Factory for the memoria turn pipeline.
///
/// FactRetrievalStage  →  ChatStage   run sequentially: retrieval must finish before
///                                    the chat agent builds its prompt.
///
/// FactExtractionStage runs fire-and-forget alongside the retrieval+chat sequence.
/// Its results land in Qdrant for the NEXT turn — this turn does not wait for it.
/// </summary>
public static class MemoriaPipelineFactory
{
    public static PipelineRunner Create(
        FactExtractorAgent     extractor,
        FactMemoryAgent        factAgent,
        MemoryAgent            chatAgent,
        Action<ExtractedFact>? onFactStored = null)
    {
        IPipelineStage[] stages =
        [
            new FactExtractionStage(extractor, factAgent, onFactStored),   // priority 10 — fire-and-forget
            new FactRetrievalStage(factAgent),               // priority 20 — must finish first
            new ChatStage(chatAgent)                         // priority 30 — uses retrieval results
        ];

        return new PipelineRunner(stages, NullLogger<PipelineRunner>.Instance);
    }
}
