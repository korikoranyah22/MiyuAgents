using Microsoft.Extensions.Logging;
using MiyuAgents.Core;
using MiyuAgents.Core.Attributes;
using MiyuAgents.Llm;
using MiyuAgents.Memory;
using MiyuAgents.Workflows;
using MishuAgents.Demo.Contracts;
using MishuAgents.Demo.Data;
using MishuAgents.Demo.Output;

namespace MishuAgents.Demo.Agents;

/// <summary>
/// Procesa los 162 fragmentos del archivo en 3 ondas. Por onda: extrae entidades,
/// sedimenta en memoria declarativa (InMemoryStore) y actualiza la memoria de
/// trabajo (MemoryWindow con decay LTP). Si topa EX-0042 sin autorización →
/// emite NeedsReplanning y el PlanExecuteStrategy vuelve a planificar (loop-back).
/// </summary>
[AgentCapability(
    Role = "análisis de expedientes",
    MemoryAccess = [MemoryKind.Episodic, MemoryKind.Declarative, MemoryKind.WorkingMemory],
    CanInitiateLlmCalls = true)]
public sealed class ExpedienteAnalystAgent : NodeAgentBase<string>
{
    readonly OperationBoard _board;
    readonly InMemoryStore<FragmentQuery, FragmentChunk> _archive;
    readonly ILlmGateway _gateway;
    readonly MemoryWindow<string> _workingMemory = new(defaultTurns: 2);

    public ExpedienteAnalystAgent(
        OperationBoard board,
        InMemoryStore<FragmentQuery, FragmentChunk> archive,
        ILlmGateway gateway,
        ILogger<AgentBase<string>> logger)
        : base(logger)
    {
        _board = board;
        _archive = archive;
        _gateway = gateway;
    }

    public override string AgentId => "expedientes";
    public override string AgentName => "Analista de Expedientes";
    public override AgentRole Role => AgentRole.Analysis;
    protected override ILlmGateway Gateway => _gateway;

    protected override async Task<string?> ExecuteCoreAsync(AgentContext ctx, CancellationToken ct)
    {
        _board.PendingReplan = false;
        _board.Findings.Clear(); // la corrida reemplaza, no acumula (idempotente ante replan)

        var quote = await ConsultArchiveAsync(ctx, "PURSUE · estado del sistema · mayo 2026", ct);
        ConsoleWriter.Agent("📁", ConsoleWriter.Dim, "expedientes", $"archivo: «{ConsoleWriter.Snippet(quote)}»");
        ConsoleWriter.Beat();

        var fragments = ExpedienteArchive.Build();
        const int waveSize = 54; // 162 / 3 ondas

        for (var wave = 0; wave < fragments.Count / waveSize; wave++)
        {
            var expired = _workingMemory.ApplyDecay(); // un turno de decay LTP por onda
            var batch = fragments.Skip(wave * waveSize).Take(waveSize).ToArray();
            var findings = new List<ExpedienteFinding>(batch.Length);
            var entities = new List<string>();

            foreach (var f in batch)
            {
                var finding = Analyze(f);
                findings.Add(finding);
                entities.AddRange(finding.Entities);
            }

            // Tachadura crítica sin autorización → replan (loop-back del PlanExecuteStrategy).
            var illegible = findings.FirstOrDefault(f => f.FragmentId == ExpedienteArchive.CriticalFragment && !f.Reconstructed);
            if (illegible is not null && _board.ReplanInstruction is null)
            {
                ConsoleWriter.Agent("📁", ConsoleWriter.Yellow, "expedientes",
                    $"onda {wave + 1}/3 · {illegible.FragmentId}: tachadura crítica — sin autorización el fragmento es ilegible. Pido replan.");
                _board.PendingReplan = true;
                return "REPLAN-REQUERIDO";
            }

            var distinct = entities.Distinct().ToArray();
            var (added, refreshed) = _workingMemory.UpdateWith(distinct.Select(e => (e, e))); // reconsolidación LTP
            var redacted = findings.Count(f => f.Redacted);

            ConsoleWriter.Agent("📁", ConsoleWriter.Cyan, "expedientes",
                $"onda {wave + 1}/3 · {batch[0].Id}..{batch[^1].Id} · {findings.Count} procesados · {redacted} con tachaduras · entidades: {Keywords.Compact(distinct)}");
            ConsoleWriter.Agent("🧠", ConsoleWriter.Dim, "expedientes",
                $"memoria de trabajo (LTP): {_workingMemory.ActiveEntries.Count} activas · {expired} envejecieron · {added} nuevas · {refreshed} reforzadas");
            _board.Findings.AddRange(findings);
            ConsoleWriter.Beat(60);
        }

        // Retrieval declarativo: preguntarle al archivo indexado por el patrón triangular.
        var hit = await _archive.SearchAsync(new FragmentQuery("TRIÁNGULO"), ct);
        if (!hit.IsEmpty)
        {
            ConsoleWriter.Agent("🔎", ConsoleWriter.Dim, "expedientes",
                $"consulta al archivo declarativo: «TRIÁNGULO» → {hit.Id} «{ConsoleWriter.Snippet(hit.Text)}»");
            ConsoleWriter.Beat();
        }

        var reconstructed = _board.Findings.Count(f => f.Reconstructed);
        var summary = $"{fragments.Count} expedientes analizados en 3 ondas · {_board.Findings.Count(f => f.Redacted)} con tachaduras · {(reconstructed == 1 ? "1 reconstruido" : $"{reconstructed} reconstruidos")}";
        var id = _board.Post(AgentId, "sintesis", "hallazgos", summary);
        ConsoleWriter.Envelope(id, AgentId, "sintesis", "hallazgos", summary);
        ConsoleWriter.Beat();

        return summary;
    }

    ExpedienteFinding Analyze(ExpedienteFragment f)
    {
        // Con autorización (replan), EX-0042 se decodifica con la heurística del anexo N7.
        var reconstructed = f.Id == ExpedienteArchive.CriticalFragment && _board.ReplanInstruction is not null;
        var entities = reconstructed
            ? ["PURSUE", "TACHADURA", "RECONSTRUIDO"]
            : Keywords.Extract(f.Body);
        return new ExpedienteFinding(f.Id, f.Source, f.Classification, entities,
            Redacted: f.Body.Contains("[CENSURADO]"), Reconstructed: reconstructed);
    }

    protected override NodeSignal ComputeSignal(NodeState state, AgentContext ctx, AgentResponse response)
        => _board.PendingReplan ? NodeSignal.NeedsReplanning : base.ComputeSignal(state, ctx, response);

    protected override string? AskFor(NodeState state, AgentContext ctx, AgentResponse response)
        => _board.PendingReplan ? "EX-0042: autorizar decodificación heurística (anexo N7)" : null;

    protected override IReadOnlyList<Artifact> ProduceArtifacts(AgentContext ctx, AgentResponse response)
        => [new Artifact("hallazgos", $"hallazgos-{_board.Findings.Count}", _board.Findings.ToArray(), AgentId)];
}
