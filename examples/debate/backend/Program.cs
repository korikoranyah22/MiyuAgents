using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Example;
using Example.Agents;
using Example.Gateways;
using Example.Strategies;
using Microsoft.Extensions.Logging.Abstractions;
using MiyuAgents.Core;
using MiyuAgents.Llm;
using MiyuAgents.Orchestration;
using MiyuAgents.Orchestration.Strategies;
using MiyuAgents.Pipeline;
using AgentRegistry = MiyuAgents.Core.AgentRegistry;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));
builder.WebHost.UseUrls("http://localhost:5000");

var app = builder.Build();
app.UseCors();

// ── In-memory sessions ────────────────────────────────────────────────────────
var sessions = new ConcurrentDictionary<string, DebateSession>();

// ── POST /api/start ───────────────────────────────────────────────────────────

app.MapPost("/api/start", (StartRequest req) =>
{
    ILlmGateway gateway = new LoggingGateway(req.Provider.ToLowerInvariant() switch
    {
        "anthropic" => (ILlmGateway)new AnthropicGateway(req.ApiKey, req.Model),
        "deepseek"  => new OpenAiGateway(req.ApiKey, req.Model, "https://api.deepseek.com"),
        _           => new OpenAiGateway(req.ApiKey, req.Model, "https://api.openai.com")
    });

    var orchestrator = new DefaultGroupOrchestrator(
        strategy:          new DebateTurnStrategy(),
        registry:          new AgentRegistry(),
        logger:            NullLogger<DefaultGroupOrchestrator>.Instance,
        maxRounds:         3,
        maxAgentsPerRound: 1);

    // Two distinct AgentBase<string> implementations — each owns its own identity and prompt.
    IAgent[] agents =
    [
        new LeftAgent(req.Model, gateway),
        new RightAgent(req.Model, gateway),
    ];

    var id = Guid.NewGuid().ToString("N");
    sessions[id] = new DebateSession(orchestrator, agents, new List<GroupMessage>(), req.Model, new SemaphoreSlim(1, 1));
    return Results.Ok(new { conversationId = id });
});

// ── POST /api/message  (Server-Sent Events) ───────────────────────────────────
//
// PoliticalAgent reads ctx.Metadata["broadcaster"] and calls SendChunkAsync after
// each LLM response — the frontend receives each agent message the moment it is ready.
//
// Event format:  data: {"sender":"🔴 Izquierda","content":"..."}\n\n
// Final event:   data: {"done":true,"historyTotal":N,"rounds":R,"latencyMs":M}\n\n

app.MapPost("/api/message", async (MessageRequest req, HttpContext http, CancellationToken ct) =>
{
    if (!sessions.TryGetValue(req.ConversationId, out var s))
    {
        http.Response.StatusCode = 404;
        await http.Response.WriteAsync("{\"error\":\"Sesión no encontrada.\"}", ct);
        return;
    }

    http.Response.ContentType                   = "text/event-stream";
    http.Response.Headers["Cache-Control"]      = "no-cache";
    http.Response.Headers["X-Accel-Buffering"]  = "no";

    var sw          = Stopwatch.StartNew();
    var broadcaster = new SseBroadcaster(http.Response);

    await s.TurnLock.WaitAsync(ct);
    try
    {
        var userMsg = new GroupMessage("Usuario", "user", req.Message, DateTime.UtcNow);
        var ctx     = AgentContext.For(req.ConversationId, Guid.NewGuid().ToString("N"), req.Message, s.Model);
        ctx.Metadata["broadcaster"] = broadcaster;

        var result = await s.Orchestrator.RunTurnAsync(
            userMessage: userMsg,
            history:     s.History,
            agents:      s.Agents,
            ctx:         ctx,
            ct:          ct);

        s.History.Add(userMsg);
        foreach (var m in result.ProducedMessages)
            s.History.Add(m);

        var done = JsonSerializer.Serialize(new
        {
            done         = true,
            historyTotal = s.History.Count,
            rounds       = result.RoundsExecuted,
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

// ── POST /api/auto  (auto-respond when user is idle) ─────────────────────────
//
// Called by the frontend after the idle timeout expires.
// Routes through DefaultGroupOrchestrator + DebateTurnStrategy so BOTH agents
// participate in the same turn-order logic as a normal user-triggered turn.
//
// A synthetic "Continúa el debate." user message is used as the orchestrator
// trigger but is NOT persisted to history — only agent responses are stored.
//
// Event format: same SSE as /api/message

app.MapPost("/api/auto", async (AutoRequest req, HttpContext http, CancellationToken ct) =>
{
    if (!sessions.TryGetValue(req.ConversationId, out var s))
    {
        http.Response.StatusCode = 404;
        await http.Response.WriteAsync("{\"error\":\"Sesión no encontrada.\"}", ct);
        return;
    }

    http.Response.ContentType                   = "text/event-stream";
    http.Response.Headers["Cache-Control"]      = "no-cache";
    http.Response.Headers["X-Accel-Buffering"]  = "no";

    var sw          = Stopwatch.StartNew();
    var broadcaster = new SseBroadcaster(http.Response);

    await s.TurnLock.WaitAsync(ct);
    try
    {
        // Synthetic trigger — gives the orchestrator a UserMessage to work with
        // without polluting the history with a fabricated "user" turn.
        var synthetic = new GroupMessage("Usuario", "user", "Continúa el debate.", DateTime.UtcNow);

        var ctx = AgentContext.For(req.ConversationId, Guid.NewGuid().ToString("N"), synthetic.Content, s.Model);
        ctx.Metadata["broadcaster"] = broadcaster;

        // Pass the full existing history; RunTurnAsync will prepend `synthetic` internally.
        // DebateTurnStrategy reads the history to determine who spoke last and picks the
        // correct opener — same logic as a normal user-triggered turn.
        var result = await s.Orchestrator.RunTurnAsync(
            userMessage: synthetic,
            history:     s.History,
            agents:      s.Agents,
            ctx:         ctx,
            ct:          ct);

        // Persist only the agent responses — NOT the synthetic user message.
        foreach (var m in result.ProducedMessages)
            s.History.Add(m);

        var done = JsonSerializer.Serialize(new
        {
            done         = true,
            historyTotal = s.History.Count,
            rounds       = result.RoundsExecuted,
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

record StartRequest(string Provider, string ApiKey, string Model);
record MessageRequest(string ConversationId, string Message);
record AutoRequest(string ConversationId);

record DebateSession(
    DefaultGroupOrchestrator Orchestrator,
    IAgent[]                  Agents,
    List<GroupMessage>        History,
    string                    Model,
    SemaphoreSlim             TurnLock);
