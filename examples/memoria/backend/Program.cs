using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Memoria;
using Memoria.Agents;
using Memoria.Gateways;
using Memoria.Memory;
using Memoria.Pipeline;
using MiyuAgents.Core;
using MiyuAgents.Llm;
using MiyuAgents.Pipeline;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));
builder.WebHost.UseUrls("http://localhost:5001");

var app = builder.Build();
app.UseCors();

var sessions = new ConcurrentDictionary<string, MemSession>();

// ── POST /api/start ───────────────────────────────────────────────────────────

app.MapPost("/api/start", async (StartRequest req, CancellationToken ct) =>
{
    ILlmGateway gateway = new LoggingGateway(req.Provider.ToLowerInvariant() switch
    {
        "anthropic" => (ILlmGateway)new AnthropicGateway(req.ApiKey, req.Model),
        "deepseek"  => new OpenAiGateway(req.ApiKey, req.Model, "https://api.deepseek.com"),
        _           => new OpenAiGateway(req.ApiKey, req.Model, "https://api.openai.com")
    });

    var embedder     = new OllamaEmbeddingClient(req.OllamaHost, req.OllamaPort, req.EmbeddingModel);
    var qdrant       = new QdrantMemoryClient(req.QdrantHost, req.QdrantPort, "test_facts");
    var factAgent    = new FactMemoryAgent(embedder, qdrant);
    var extractAgent = new FactExtractorAgent(req.Model, gateway);
    var chatAgent    = new MemoryAgent(req.Model, gateway);

    await factAgent.InitializeAsync(ct);

    // Create memories list first so the fire-and-forget callback can reference it.
    var memories = new List<StoredMemory>();

    var pipeline = MemoriaPipelineFactory.Create(extractAgent, factAgent, chatAgent,
        onFactStored: f =>
        {
            var m = new StoredMemory(Guid.NewGuid().ToString(), f.Clave, f.Valor, "auto");
            lock (memories) memories.Add(m);
        });

    var id = Guid.NewGuid().ToString("N");
    sessions[id] = new MemSession(
        Pipeline:  pipeline,
        FactAgent: factAgent,
        Model:     req.Model,
        History:   [],
        Memories:  memories,
        TurnLock:  new SemaphoreSlim(1, 1));

    return Results.Ok(new { conversationId = id });
});

// ── POST /api/memory ──────────────────────────────────────────────────────────
// Manual fact entry from the UI.

app.MapPost("/api/memory", async (MemoryRequest req, CancellationToken ct) =>
{
    if (!sessions.TryGetValue(req.ConversationId, out var s))
        return Results.NotFound(new { error = "Sesión no encontrada." });

    try
    {
        await s.FactAgent.StoreFactAsync(req.Label, req.Value, ct);
        var memory = new StoredMemory(Guid.NewGuid().ToString(), req.Label, req.Value, "manual");
        s.Memories.Add(memory);
        return Results.Ok(memory);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error al guardar el recuerdo: {ex.Message}");
    }
});

// ── GET /api/memories ─────────────────────────────────────────────────────────

app.MapGet("/api/memories", (string conversationId) =>
{
    if (!sessions.TryGetValue(conversationId, out var s))
        return Results.NotFound(new { error = "Sesión no encontrada." });
    lock (s.Memories) return Results.Ok(s.Memories.ToList());
});

// ── POST /api/message  (Server-Sent Events) ───────────────────────────────────

app.MapPost("/api/message", async (MessageRequest req, HttpContext http, CancellationToken ct) =>
{
    if (!sessions.TryGetValue(req.ConversationId, out var s))
    {
        http.Response.StatusCode = 404;
        await http.Response.WriteAsync("{\"error\":\"Sesión no encontrada.\"}", ct);
        return;
    }

    http.Response.ContentType                  = "text/event-stream";
    http.Response.Headers["Cache-Control"]     = "no-cache";
    http.Response.Headers["X-Accel-Buffering"] = "no";

    var broadcaster = new SseBroadcaster(http.Response);
    var pipeCtx = new PipelineContext
    {
        EventBus    = NullAgentEventBus.Instance,
        Broadcaster = broadcaster
    };

    var sw = Stopwatch.StartNew();
    await s.TurnLock.WaitAsync(ct);
    try
    {
        var ctx = AgentContext.For(req.ConversationId, Guid.NewGuid().ToString("N"), req.Message, s.Model);
        var ctxWithHistory = ctx with { History = s.History };

        var result = await s.Pipeline.RunAsync(ctxWithHistory, pipeCtx, ct);

        // Persist turn to history.
        s.History.Add(new ConversationMessage("user", req.Message));
        if (result.Response is { Length: > 0 } text)
            s.History.Add(new ConversationMessage("assistant", text));

        var done = JsonSerializer.Serialize(new
        {
            done         = true,
            historyTotal = s.History.Count,
            memoriesUsed = ctxWithHistory.Results.Facts.Count,
            latencyMs    = (int)sw.ElapsedMilliseconds
        });
        await http.Response.WriteAsync($"data: {done}\n\n", ct);
        await http.Response.Body.FlushAsync(ct);
    }
    finally
    {
        s.TurnLock.Release();
    }
});

app.Run();

// ── Models ────────────────────────────────────────────────────────────────────

record StartRequest(
    string Provider,
    string ApiKey,
    string Model,
    string QdrantHost,
    int    QdrantPort,
    string OllamaHost,
    int    OllamaPort,
    string EmbeddingModel);

record MessageRequest(string ConversationId, string Message);
record MemoryRequest(string ConversationId, string Label, string Value);
record StoredMemory(string Id, string Label, string Value, string Source);

// ── Session ───────────────────────────────────────────────────────────────────

sealed class MemSession(
    PipelineRunner            Pipeline,
    FactMemoryAgent           FactAgent,
    string                    Model,
    List<ConversationMessage> History,
    List<StoredMemory>        Memories,
    SemaphoreSlim             TurnLock)
{
    public PipelineRunner            Pipeline  { get; } = Pipeline;
    public FactMemoryAgent           FactAgent { get; } = FactAgent;
    public string                    Model     { get; } = Model;
    public List<ConversationMessage> History   { get; } = History;
    public List<StoredMemory>        Memories  { get; } = Memories;
    public SemaphoreSlim             TurnLock  { get; } = TurnLock;
}
