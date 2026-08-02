using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MiyuAgents.Core;
using MiyuAgents.Llm;
using MiyuAgents.Memory;
using MiyuAgents.Pipeline;
using MiyuAgents.Workflows;
using MishuAgents.Demo.Agents;
using MishuAgents.Demo.Contracts;
using MishuAgents.Demo.Data;
using MishuAgents.Demo.Output;
using MishuAgents.Demo.Swarm;
using AgentRegistry = MiyuAgents.Core.AgentRegistry;

// Locale rioplatense para los números: 0,98 y 98 % en vez de 0.98 (el demo es en español).
CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-AR");

try { Console.OutputEncoding = Encoding.UTF8; } catch { /* terminal sin UTF-8: seguimos igual */ }

// ── Infraestructura: pizarrón, archivo declarativo y gateway ─────────────────
var board = new OperationBoard();
var archive = new InMemoryStore<FragmentQuery, FragmentChunk>();
var gateway = new PursuitArchiveGateway();

// ── DI + descubrimiento por atributos (AgentRegistry / [AgentCapability]) ────
var services = new ServiceCollection();
services.AddSingleton<OperationBoard>(board);
services.AddSingleton(archive);
services.AddSingleton<PursuitArchiveGateway>(gateway);
services.AddSingleton<ILlmGateway>(gateway);
services.AddSingleton(typeof(ILogger<>), typeof(ConsoleLogger<>));

var registry = new AgentRegistry();
registry.RegisterFromAssembly(typeof(Program).Assembly, services);
await using var sp = services.BuildServiceProvider();

var mishu = sp.GetRequiredService<MishuCoordinatorAgent>();
var analyst = sp.GetRequiredService<ExpedienteAnalystAgent>();
var tracer = sp.GetRequiredService<TriangleTracerAgent>();
var detector = sp.GetRequiredService<InfiltratorDetectorAgent>();
var synth = sp.GetRequiredService<SynthesizerAgent>();

// ── Fase 0 · apertura y carga del archivo ────────────────────────────────────
Banner();

ConsoleWriter.Info("▸ cargando archivo PURSUE en memoria declarativa (InMemoryStore)…");
var fragments = ExpedienteArchive.Build();
foreach (var f in fragments)
    await archive.StoreAsync(
        new FragmentChunk(f.Id, f.Source, f.Classification, f.Body, Keywords.Extract(f.Body)),
        CancellationToken.None);
ConsoleWriter.Info($"▸ {fragments.Count} expedientes indexados · {fragments.Count(f => f.Body.Contains("[CENSURADO]"))} con tachaduras · 1 con tachadura crítica.");
ConsoleWriter.Beat(140);

Roster(registry);

ConsoleWriter.Line();
ConsoleWriter.DimLine("Mishu se suscribe a los lifecycle events de los especialistas (monitoreo)…");
mishu.AttachMonitoring([analyst, tracer, detector, synth]);
ConsoleWriter.Beat(100);

// ── Fase 1 · la operación: un solo workflow, tres niveles de recursión ───────
var root = SwarmWorkflow.Build(sp);
var trace = new SwarmTraceSink();

ConsoleWriter.Line();
ConsoleWriter.Info("▸ lanzando OPERACIÓN TRIÁNGULO — el enjambre corre como un solo workflow…");
ConsoleWriter.Beat(180);

var sw = Stopwatch.StartNew();
NodeResult result;
using (NodeTrace.Begin(trace))
{
    result = await root.RunNodeAsync(new NodeState
    {
        Input = "OPERACIÓN TRIÁNGULO · 162 expedientes desclasificados · mayo 2026",
    });
}
sw.Stop();

// ── Fase 2 · resumen del árbol ───────────────────────────────────────────────
ConsoleWriter.Line();
ConsoleWriter.Info("▸ resumen del árbol (WorkflowNode)");
ConsoleWriter.Raw($"   · señal final: {result.Signal} · replans: {board.ReplanCount - 1} · latencia del workflow: {sw.Elapsed.TotalSeconds:F1} s");
var finalArtifacts = result.Artifacts
    .GroupBy(a => a.Kind)
    .Select(g => g.Last().Name)
    .Where(n => n is not null);
ConsoleWriter.Raw($"   · artefactos finales: {string.Join(" · ", finalArtifacts)}");
ConsoleWriter.Beat(140);

// ── Fase 3 · el expediente desclasificado ────────────────────────────────────
if (board.Report is { } report)
    ReportFormatter.Print(report, board.ReportAnexo);

// ── Fase 4 · el giro ─────────────────────────────────────────────────────────
mishu.Reveal();
ReportFormatter.PrintFirmaCorrection(board.ReportFirma ?? "[CENSURADO]");

// ── Fase 5 · estadísticas del operativo ──────────────────────────────────────
Stats(board, gateway, trace);

// ── Epílogo ──────────────────────────────────────────────────────────────────
ConsoleWriter.Line();
ConsoleWriter.DimLine("▸ el portal WAR.GOV/UFO actualizó la página: «no hay registros».");
ConsoleWriter.DimLine("▸ los 162 expedientes fueron re-clasificados como «rutina administrativa». — PURSUE, 03:12");
ConsoleWriter.Line();
ConsoleWriter.Raw($"  {ConsoleWriter.Col(ConsoleWriter.Bold, "El enjambre siguió funcionando después del informe.")}");
ConsoleWriter.Raw($"  {ConsoleWriter.Col(ConsoleWriter.Bold, "Nadie lo nota. Eso es exactamente lo que un buen coordinador hace.")}");
ConsoleWriter.Line();

return 0;

// ── Helpers de escenografía ──────────────────────────────────────────────────

static void Banner()
{
    ConsoleWriter.Raw(ConsoleWriter.Col(ConsoleWriter.Cyan, """
    ╔══════════════════════════════════════════════════════════════════════════╗
    ║   OPERACIÓN TRIÁNGULO · expedientes desclasificados · mayo 2026           ║
    ║   enjambre Mishu Agents · corriendo sobre MiyuAgents (src/)               ║
    ╚══════════════════════════════════════════════════════════════════════════╝
    """));
    ConsoleWriter.Line();
    ConsoleWriter.DimLine("El portal WAR.GOV/UFO liberó 162 expedientes del sistema PURSUE.");
    ConsoleWriter.DimLine("Hay un patrón triangular, un androide infiltrado y un secretario que no figura en ninguna nómina.");
    ConsoleWriter.DimLine("Vamos a verlo en vivo. Sin cortes. Con el framework en el medio.");
    ConsoleWriter.Line();
}

static void Roster(AgentRegistry registry)
{
    ConsoleWriter.Line();
    ConsoleWriter.Raw($"  {ConsoleWriter.Col(ConsoleWriter.Bold, "NÓMINA DE AGENTES")} · descubiertos por AgentRegistry ([AgentCapability])");
    foreach (var r in registry.GetAll().OrderBy(r => r.Capability.Role))
        ConsoleWriter.Raw($"   · {ConsoleWriter.Col(ConsoleWriter.Cyan, r.Capability.Role.PadRight(30))} {r.Type.Name}");
    ConsoleWriter.Line();
}

static void Stats(OperationBoard board, PursuitArchiveGateway gateway, SwarmTraceSink trace)
{
    var snap = gateway.GetStats().Snapshot();
    var tracker = new TokenTracker(128_000);
    tracker.Record(new LlmUsage((int)snap.InputTokens, (int)snap.OutputTokens));

    ConsoleWriter.Line();
    ConsoleWriter.Info("▸ estadísticas del operativo");
    ConsoleWriter.Raw($"   · envelopes en el bus (OperationBoard): {board.MessageCount}");
    ConsoleWriter.Raw($"   · eventos de trace: {trace.Events.Count} · lanes distintos: {trace.Events.Select(e => e.Lane).Distinct().Count()}");
    ConsoleWriter.Raw($"   · gateway pursue-archive: {snap.CallCount} llamadas · {snap.InputTokens} tok in · {snap.OutputTokens} tok out · {snap.ErrorCount} errores");
    ConsoleWriter.Raw($"   · TokenTracker: {tracker.TotalTokens} tokens · contexto usado: {tracker.ContextUsageRatio:P2}");
    ConsoleWriter.Raw($"   · archivo declarativo: {ExpedienteArchive.Total} chunks · memoria de trabajo: decay LTP activo");
}
