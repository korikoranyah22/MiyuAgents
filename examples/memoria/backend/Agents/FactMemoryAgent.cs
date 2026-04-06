using Memoria.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using MiyuAgents.Core;
using MiyuAgents.Memory;

namespace Memoria.Agents;

/// <summary>
/// Memory agent that embeds the current user message, searches Qdrant for
/// semantically relevant facts, and writes them into ctx.Results.Facts.
///
/// Extends MemoryAgentBase so the full framework pipeline runs:
///   BuildQueryAsync → Store.SearchAsync → PostProcessAsync → PopulateAccumulator → event
/// </summary>
public sealed class FactMemoryAgent(
    OllamaEmbeddingClient embedder,
    QdrantMemoryClient    store)
    : MemoryAgentBase<FactQuery, List<FactHit>>(
        NullLogger<MemoryAgentBase<FactQuery, List<FactHit>>>.Instance)
{
    public override string     AgentId    => "agent-fact-memory";
    public override string     AgentName  => "FactMemory";
    public override AgentRole  Role       => AgentRole.Memory;
    public override MemoryKind MemoryKind => MemoryKind.Declarative;

    protected override IMemoryStore<FactQuery, List<FactHit>> Store      => store;
    protected override IEmbeddingProvider                     Embeddings => embedder;

    // ── MemoryAgentBase template method overrides ─────────────────────────────

    protected override async Task<FactQuery?> BuildQueryAsync(AgentContext ctx, CancellationToken ct)
    {
        var vector = await embedder.EmbedAsync(ctx.UserMessage, ct);
        return new FactQuery(vector);
    }

    protected override void PopulateAccumulator(List<FactHit> result, AgentContext ctx)
        => ctx.Results.Facts.AddRange(result);

    protected override bool IsEmpty(List<FactHit> result) => result.Count == 0;

    /// <summary>
    /// Consolidation store (not used in this example).
    /// The example stores facts explicitly via StoreFactAsync.
    /// </summary>
    public override Task StoreAsync(List<FactHit> data, AgentContext ctx, CancellationToken ct)
        => Task.CompletedTask;

    // ── Public helpers for Program.cs ─────────────────────────────────────────

    /// <summary>
    /// Embed a label+value fact and persist it to Qdrant.
    /// Called by POST /api/memory.
    /// </summary>
    public async Task StoreFactAsync(string label, string value, CancellationToken ct)
    {
        var text   = $"{label}: {value}";
        var vector = await embedder.EmbedAsync(text, ct);
        await store.StoreAsync(new FactEntry(label, value, text, vector), ct);
    }

    /// <summary>
    /// Delete and recreate the collection so a new session starts clean.
    /// Called by POST /api/start.
    /// </summary>
    public Task InitializeAsync(CancellationToken ct) =>
        store.DeleteCollectionAsync(ct);
}
