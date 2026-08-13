# MiyuAgents

A .NET 10 framework for building multi-agent LLM systems with event-driven lifecycle, cognitive memory patterns, and composable orchestration.

**Design goals:** zero transitive dependencies · async-first · empty responses are valid · interfaces over implementations · immutable context · optional event sourcing.

---

## Table of Contents

1. [Installation](#installation)
2. [Quick Start](#quick-start)
3. [Core Concepts](#core-concepts)
   - [IAgent and AgentBase](#iagent-and-agentbase)
   - [AgentContext](#agentcontext)
   - [AgentResponse](#agentresponse)
4. [Memory System](#memory-system)
   - [MemoryWindow — LTP Decay](#memorywindow--ltp-decay)
   - [ConsolidationChain](#consolidationchain)
   - [InMemoryStore and CachingMemoryStore](#inmemorystore-and-cachingmemorystore)
5. [LLM Gateway](#llm-gateway)
   - [ILlmGateway](#illmgateway)
   - [TokenTracker](#tokentracker)
   - [LlmGatewayRouter](#llmgatewayrouter)
   - [GatewayBase](#gatewaybase)
   - [LoremGateway — development fallback](#loremgateway--development-fallback)
6. [Pipeline](#pipeline)
   - [IPipelineStage](#ipipipelinestage)
   - [PipelineRunner](#pipelinerunner)
   - [ParallelAgentStage](#parallelagentstage)
   - [Built-in Stage Utilities](#built-in-stage-utilities)
7. [Orchestration](#orchestration)
   - [ITurnOrchestrator](#iturnorchestrator)
   - [IGroupOrchestrator](#igrouporchestrator)
   - [Round Decision Strategies](#round-decision-strategies)
8. [Group Conversations](#group-conversations)
   - [Participants](#participants)
   - [ITurnPolicy](#iturnpolicy)
   - [IParticipantRouter](#iparticipantrouter)
   - [IGroupConversationOrchestrator](#igroupconversationorchestrator)
9. [Composite Agents](#composite-agents)
10. [Dependency Injection](#dependency-injection)
11. [Examples](#examples)
12. [Priority Reference](#priority-reference)

---

## Installation

```xml
<!-- MiyuAgents.csproj -->
<PackageReference Include="MiyuAgents" Version="1.0.0" />
```

The core package has **no transitive dependencies**. Add `Microsoft.Extensions.Logging` if you want structured logging in base classes.

---

## Quick Start

```csharp
// 1. Implement an agent
public class GreetingAgent : AgentBase<string>
{
    public GreetingAgent(ILogger<AgentBase<string>> logger) : base(logger) { }

    public override string AgentId   => "greeting";
    public override string AgentName => "Greeting Agent";
    public override AgentRole Role   => AgentRole.Conversation;

    protected override Task<string?> ExecuteCoreAsync(AgentContext ctx, CancellationToken ct)
    {
        var reply = $"Hello! You said: {ctx.UserMessage}";
        ctx.Results.LlmResponse = reply;
        return Task.FromResult<string?>(reply);
    }
}

// 2. Wire up DI and run
var services = new ServiceCollection()
    .AddLogging()
    .AddMiyuAgents()
    .AddLoremGateway()
    .BuildServiceProvider();

var agent  = new GreetingAgent(services.GetRequiredService<ILogger<AgentBase<string>>>());
var ctx    = AgentContext.For("conv-1", "msg-1", "Hi there!");
var result = await agent.ProcessAsync(ctx);

Console.WriteLine(result.As<string>()); // "Hello! You said: Hi there!"
```

---

## Core Concepts

### IAgent and AgentBase

Every agent implements `IAgent`:

```csharp
public interface IAgent
{
    string    AgentId   { get; }
    string    AgentName { get; }
    AgentRole Role      { get; }

    // Lifecycle events — subscribe for observability
    event AsyncEventHandler<MessageReceivedEventArgs>?       OnMessageReceived;
    event AsyncEventHandler<LlmCallRequestedEventArgs>?      OnLLMCallRequested;
    event AsyncEventHandler<LlmCallRespondedEventArgs>?      OnLLMCallResponded;
    event AsyncEventHandler<AgentResponseProducedEventArgs>? OnResponseProduced;
    event AsyncEventHandler<AgentErrorEventArgs>?            OnError;

    Task<AgentResponse> ProcessAsync(AgentContext ctx, CancellationToken ct = default);
}
```

`AgentBase<TResult>` implements the Template Method pattern. Override `ExecuteCoreAsync` — the base handles lifecycle events, timing, and errors automatically:

```csharp
[AgentCapability(Role = "Answers questions about math")]
public class MathAgent : AgentBase<string>
{
    private readonly ILlmGateway _llm;

    public MathAgent(ILlmGateway llm, ILogger<AgentBase<string>> logger) : base(logger)
        => _llm = llm;

    public override string AgentId   => "math-agent";
    public override string AgentName => "Math Agent";
    public override AgentRole Role   => AgentRole.Conversation;

    protected override async Task<string?> ExecuteCoreAsync(AgentContext ctx, CancellationToken ct)
    {
        var req = new LlmRequest
        {
            Model    = ctx.Model,
            Messages = [new("user", ctx.UserMessage)]
        };

        var response = await _llm.CompleteAsync(req, ct);
        ctx.Results.LlmResponse = response.Content;
        return response.Content;
    }
}
```

Subscribe to lifecycle events for logging or tracing:

```csharp
agent.OnLLMCallRequested += async (_, args) =>
    Console.WriteLine($"[{args.AgentId}] calling LLM with model {args.Model}");

agent.OnResponseProduced += async (_, args) =>
    Console.WriteLine($"[{args.AgentId}] responded in {args.Latency.TotalMilliseconds}ms");
```

### AgentContext

`AgentContext` is a **sealed immutable record** — the complete snapshot of a single turn:

```csharp
// Factory method for simple use
var ctx = AgentContext.For("conv-123", "msg-456", "What is 2+2?");

// With full options
var ctx = new AgentContext
{
    ConversationId = "conv-123",
    MessageId      = "msg-456",
    ProfileId      = "user-001",
    CharacterId    = "naira",
    UserMessage    = "What is 2+2?",
    History        = previousMessages,
    IsFirstTurn    = false,
    Model          = "deepseek-chat",
    Metadata       = new Dictionary<string, object> { ["debug"] = true }
};
```

The `Results` accumulator is the **mutable** part — agents write their outputs here:

```csharp
// Any agent in the pipeline can read what previous agents wrote
ctx.Results.EmotionsBefore    // set by EmotionAgent
ctx.Results.EpisodicMemories  // set by MemoryRetrievalAgent
ctx.Results.LlmResponse       // set by ConversationAgent
ctx.Results.ImageDescription  // set by VisionAgent
```

### AgentResponse

```csharp
// Check response
if (response.IsEmpty)  { /* agent had nothing to say */ }
if (response.IsError)  { /* something failed */ }

// Extract typed data
var text    = response.As<string>();
var summary = response.As<MemorySummary>();

// Access metadata
Console.WriteLine($"Latency: {response.Latency.TotalMilliseconds}ms");
Console.WriteLine($"Agent:   {response.AgentName}");
```

---

## Memory System

MiyuAgents models memory after neuroscience:

| Memory Kind   | Analogy          | Implementation                      |
|---------------|------------------|-------------------------------------|
| Episodic      | Hippocampus       | Vector store + LTP decay            |
| Declarative   | Neocortex         | Deterministic GUID upsert           |
| Working       | Prefrontal cortex | MemoryWindow (per-session)          |
| Crystallized  | Cerebellum        | Chunked document retrieval          |

Implement a memory agent by extending `MemoryAgentBase<TQuery, TResult>`:

```csharp
public class EpisodicMemoryAgent : MemoryAgentBase<string, MemorySummary[]>
{
    private readonly IMemoryStore<string, MemorySummary> _store;
    private readonly IEmbeddingProvider _embedder;

    public EpisodicMemoryAgent(
        IMemoryStore<string, MemorySummary> store,
        IEmbeddingProvider embedder,
        ILogger<MemoryAgentBase<string, MemorySummary[]>> logger)
        : base(logger)
    {
        _store   = store;
        _embedder = embedder;
    }

    public override string AgentId   => "episodic-memory";
    public override string AgentName => "Episodic Memory";
    public override AgentRole Role   => AgentRole.Memory;

    protected override async Task<MemorySummary[]?> RetrieveCoreAsync(
        string query, AgentContext ctx, CancellationToken ct)
    {
        var results = await _store.QueryAsync(query, ct);
        ctx.Results.EpisodicMemories.AddRange(results);
        return [.. results];
    }

    protected override async Task StoreCoreAsync(
        AgentContext ctx, CancellationToken ct)
    {
        if (ctx.Results.LlmResponse is null) return;
        var summary = new MemorySummary(ctx.ConversationId, ctx.Results.LlmResponse);
        await _store.StoreAsync(summary, ct);
    }
}
```

### MemoryWindow — LTP Decay

Models working memory with turn-based decay (Long-Term Potentiation model). Each turn without retrieval decrements the entry's counter; at zero it is evicted. Retrieval resets the counter (reconsolidation).

```csharp
// Entries live for 3 turns by default before expiring
var window = new MemoryWindow<string>(defaultTurns: 3);

// Add or refresh entries (retrieved from long-term storage)
window.UpdateWith([
    ("mem-1", "user likes coffee"),
    ("mem-2", "user is vegetarian"),
]);
// Returns (New: 2, Refreshed: 0)

// Read what's still active
IReadOnlyList<string> active = window.ActiveEntries;

// Call once per turn — decrements all counters, removes expired entries
int expired = window.ApplyDecay();

// If an entry is retrieved again, UpdateWith resets its counter (reconsolidation)
window.UpdateWith([("mem-1", "user likes coffee")]); // TurnsRemaining reset to 3
```

### ConsolidationChain

Runs memory consolidation after delivery (sleep-like background process):

```csharp
// Register handlers
var chain = new ConsolidationChain(
[
    new EpisodicSummaryHandler(memoryService),
    new FactExtractionHandler(factService),
    new ContinuityHandler(continuityService),
],
logger);

// Trigger after conversation turn completes (fire-and-forget)
_ = Task.Run(() => chain.ConsolidateAsync(exchange, ctx, CancellationToken.None));
```

### InMemoryStore and CachingMemoryStore

For development and testing without a vector database:

```csharp
// Pure in-memory store
var store = new InMemoryStore<string, MemorySummary>();
await store.StoreAsync(new MemorySummary("user likes tea"), ct);

var results = await store.QueryAsync("beverages", ct);

// LRU caching decorator over any IMemoryStore
var cachedStore = new CachingMemoryStore<string, MemorySummary>(
    inner:    realQdrantStore,
    capacity: 512);
```

---

## LLM Gateway

### ILlmGateway

All LLM access goes through a single interface:

```csharp
public interface ILlmGateway
{
    Task<LlmResponse>             CompleteAsync(LlmRequest request, CancellationToken ct);
    IAsyncEnumerable<LlmChunk>    StreamAsync(LlmRequest request, CancellationToken ct);
    Task<float[]>                 EmbedAsync(string text, CancellationToken ct);
    LlmGatewayStatsSnapshot       GetStats();
}
```

Crafting a request:

```csharp
var request = new LlmRequest
{
    Model        = "deepseek-chat",
    SystemPrompt = "You are a helpful assistant.",
    Messages     =
    [
        new("user",      "What is the capital of France?"),
        new("assistant", "Paris."),
        new("user",      "And of Spain?"),
    ],
    Temperature = 0.7f,
    MaxTokens   = 512,
    // Vision support
    Images          = [base64Image],
    ImageMediaTypes = ["image/jpeg"],
};

var response = await gateway.CompleteAsync(request, ct);
Console.WriteLine(response.Content);
Console.WriteLine($"Tokens: {response.Usage?.InputTokens} in / {response.Usage?.OutputTokens} out");
```

`ConversationMessage` has an optional `Name` field for multi-agent conversations where multiple participants share the same role. When provided, gateways pass it as the native speaker-identity field supported by the LLM API (OpenAI `name`, Anthropic content labeling), so the model can distinguish who said what without embedding speaker tags in the content string:

```csharp
// Two agents both writing as "assistant" — Name tells the model who is who
Messages =
[
    new("user",      "What should we do about inflation?"),
    new("assistant", "Raise interest rates.",  Name: "hawk_agent"),
    new("assistant", "Increase spending.",     Name: "dove_agent"),
    new("user",      "hawk_agent, respond to that."),
]
```

`DefaultGroupOrchestrator` sets `Name` automatically from `GroupMessage.Sender` when building each agent's history context.

Streaming:

```csharp
await foreach (var chunk in gateway.StreamAsync(request, ct))
{
    if (chunk.IsComplete) break;
    Console.Write(chunk.Delta);
}
```

Observability via stats:

```csharp
var stats = gateway.GetStats();
Console.WriteLine($"Calls: {stats.CallCount}  Errors: {stats.ErrorCount}");
Console.WriteLine($"Tokens in: {stats.InputTokens}  out: {stats.OutputTokens}");
Console.WriteLine($"Cache hits: {stats.CacheHitTokens}  misses: {stats.CacheMissTokens}");
Console.WriteLine($"Cache hit rate: {stats.CacheHitRate:P1}");
```

For multi-provider aggregation via `LlmGatewayRouter`:

```csharp
var allStats = router.AggregatedStats();
foreach (var (provider, snap) in allStats)
    Console.WriteLine($"{provider}: {snap.CallCount} calls, {snap.InputTokens + snap.OutputTokens} tokens");
```

### GatewayBase

Extend `GatewayBase` to implement a concrete LLM provider:

```csharp
public class OpenAiGateway : GatewayBase
{
    private readonly HttpClient _http;
    private readonly string     _apiKey;

    public OpenAiGateway(HttpClient http, string apiKey) : base("openai")
    {
        _http   = http;
        _apiKey = apiKey;
    }

    protected override async Task<LlmResponse> CompleteCoreAsync(
        LlmRequest request, CancellationToken ct)
    {
        // Serialize to OpenAI format, send, deserialize
        var payload  = BuildPayload(request);
        var httpResp = await _http.PostAsJsonAsync("/v1/chat/completions", payload, ct);
        var body     = await httpResp.Content.ReadFromJsonAsync<OpenAiResponse>(ct);
        return new LlmResponse(body!.Choices[0].Message.Content, MapUsage(body.Usage));
    }

    protected override async IAsyncEnumerable<LlmChunk> StreamCoreAsync(
        LlmRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        // SSE streaming implementation
        yield return new LlmChunk("...", IsComplete: false);
    }

    protected override async Task<float[]> EmbedCoreAsync(string text, CancellationToken ct)
    {
        // Embedding API call
        return Array.Empty<float>();
    }
}
```

### TokenTracker

Per-conversation token accounting with context-window awareness:

```csharp
var tracker = new TokenTracker(contextWindowSize: 128_000);

// Record usage after each LLM call
tracker.Record(new LlmUsage(InputTokens: 4_200, OutputTokens: 850));

// Check totals
Console.WriteLine($"Input: {tracker.TotalInputTokens}  Output: {tracker.TotalOutputTokens}");
Console.WriteLine($"Context used: {tracker.ContextUsageRatio:P0}");  // e.g. "40%"

// Guard thresholds (defaults: soft=0.80, hard=0.95)
if (tracker.IsApproachingSoftLimit())
    TruncateHistory(ctx);
if (tracker.IsApproachingHardLimit())
    throw new ContextWindowExceededException();

// Custom threshold
if (tracker.IsApproachingSoftLimit(threshold: 0.70))
    WarnUser("getting close to context limit");

// Immutable snapshot for logging / billing
TokenTrackerSnapshot snap = tracker.Snapshot();
```

### LlmGatewayRouter

Routes each request to the correct gateway based on the model name. Resolution order:
1. **Exact match** — model name is in `gateway.SupportedModels`
2. **Partial provider match** — model string contains `gateway.ProviderName`
3. **Default fallback** — first registered gateway

```csharp
// Each gateway declares which models it supports
public class DeepSeekGateway : GatewayBase
{
    public override string ProviderName => "deepseek";
    public override IReadOnlyList<string> SupportedModels =>
        ["deepseek-chat", "deepseek-r1", "deepseek-reasoner"];
    // ...
}

// Register all gateways — router resolves based on model at call time
var router = new LlmGatewayRouter([deepSeekGateway, anthropicGateway, openAiGateway]);

// Routing examples:
router.Resolve("deepseek-chat")    // → deepSeekGateway (exact match)
router.Resolve("deepseek-r2")      // → deepSeekGateway (partial: "deepseek" in model)
router.Resolve("claude-sonnet-4-6") // → anthropicGateway (exact match)
router.Resolve("unknown-model")     // → deepSeekGateway (first registered)
```

In DI, `LlmGatewayRouter` is automatically populated from all registered `ILlmGateway` implementations.

### LoremGateway — development fallback

Use `LoremGateway` when you have no API key. It returns deterministic lorem ipsum text and reproducible embeddings:

```csharp
// In DI
services.AddLoremGateway();

// Direct usage
var gateway = new LoremGateway();
var response = await gateway.CompleteAsync(request, ct);
// response.Content → "Lorem ipsum dolor sit amet..."

var embedding = await gateway.EmbedAsync("any text", ct);
// embedding → deterministic 768-dim normalized vector (same input → same vector)
```

---

## Pipeline

### IPipelineStage

Stages are the Chain of Responsibility units. Order them with `Priority`:

```csharp
public class EmotionAnalysisStage : IPipelineStage
{
    private readonly IEmotionAnalyzer _analyzer;

    public EmotionAnalysisStage(IEmotionAnalyzer analyzer)
        => _analyzer = analyzer;

    public string Name     => "EmotionAnalysis";
    public int    Priority => 300; // Analysis tier

    public async Task<PipelineStageResult> ExecuteAsync(
        AgentContext ctx, PipelineContext pipeline, CancellationToken ct)
    {
        var emotions = await _analyzer.AnalyzeAsync(ctx.UserMessage, ct);
        ctx.Results.EmotionsBefore = emotions;

        await pipeline.EventBus.PublishAsync(
            new EmotionAnalyzedEvent(ctx.MessageId, emotions), ct);

        return PipelineStageResult.Continue(Name);
    }
}
```

Abort the pipeline early:

```csharp
if (string.IsNullOrWhiteSpace(ctx.UserMessage))
    return PipelineStageResult.Abort(Name, "empty message — skipping pipeline");
```

### PipelineRunner

```csharp
var runner = new PipelineRunner(
[
    new EmotionAnalysisStage(emotionAnalyzer),
    new MemoryRetrievalStage(memoryAgent),
    new LlmCallStage(gateway),
    new DeliveryStage(broadcaster),
]);

var pipeline = new PipelineContext
{
    EventBus    = new ConsoleAgentEventBus(),
    Broadcaster = new NullBroadcaster(),
};

var result = await runner.RunAsync(ctx, pipeline, ct);

foreach (var step in result.StageHistory)
    Console.WriteLine($"{step.StageName}: {step.Outcome} ({step.DurationMs}ms)");
```

### ParallelAgentStage

Runs multiple agents with `Task.WhenAll` and collects all results. If any agent returns an error response, the stage throws `AggregateException` so the pipeline can react:

```csharp
// Memory, knowledge, and vision agents all run at the same time
var stage = new ParallelAgentStage(
    name:     "parallel-retrieval",
    priority: 200,
    episodicMemoryAgent,
    factAgent,
    knowledgeBaseAgent);
```

Subscribe to `AllAgentsInStageCompleted` on the event bus to observe when all parallel agents finish:

```csharp
await pipeline.EventBus.PublishAsync(new AllAgentsInStageCompleted(
    stageName, conversationId, messageId, agentIds), ct);
```

### Built-in Stage Utilities

**AbortIfEmptyStage** — abort if a required field is missing:

```csharp
// Abort the pipeline if no LLM response was produced
new AbortIfEmptyStage(
    stageName:   "response-guard",
    priority:    800,
    isEmpty:     results => string.IsNullOrEmpty(results.LlmResponse),
    abortReason: "no LLM response produced")
```

**ConditionalStage** — wrap any stage with a condition:

```csharp
// Only run vision analysis when the message contains an image
new ConditionalStage(
    condition: ctx => ctx.ImageBytes is not null,
    inner:     new VisionAnalysisStage(visionAgent))
```

**RetryStage** — retry on failure with exponential backoff:

```csharp
// Retry the LLM call up to 3 times (200ms, 400ms backoff)
new RetryStage(
    inner:       new LlmCallStage(gateway),
    maxAttempts: 3,
    baseDelay:   TimeSpan.FromMilliseconds(200))
```

**TimedStage** — abort cleanly if a stage takes too long:

```csharp
// Cancel memory retrieval after 2 seconds — don't block the user
new TimedStage(
    inner:   new MemoryRetrievalStage(memoryAgent),
    timeout: TimeSpan.FromSeconds(2))
```

Combining utilities:

```csharp
var pipeline = new PipelineRunner(
[
    new ConditionalStage(
        ctx => ctx.ImageBytes is not null,
        new TimedStage(new VisionStage(vision), TimeSpan.FromSeconds(5))),

    new RetryStage(new MemoryRetrievalStage(memory), maxAttempts: 2, baseDelay: TimeSpan.FromMilliseconds(100)),

    new LlmCallStage(gateway),

    new AbortIfEmptyStage("LlmResponse", ctx => ctx.Results.LlmResponse),

    new DeliveryStage(broadcaster),
]);
```

Event bus implementations:

```csharp
// No-op (default / production — use your own event bus)
services.AddSingleton<IAgentEventBus, NullAgentEventBus>();

// Console printer (development)
services.AddSingleton<IAgentEventBus, ConsoleAgentEventBus>();
```

---

## Orchestration

### ITurnOrchestrator

Single-user, sequential turn execution:

```csharp
var orchestrator = new DefaultTurnOrchestrator(pipelineRunner, eventBus, broadcaster);
var turnResult   = await orchestrator.ExecuteTurnAsync(ctx, ct);
```

### IGroupOrchestrator

Multi-agent, stateless turn — the orchestrator decides who speaks each round:

```csharp
var orchestrator = new DefaultGroupOrchestrator(
    strategy:          new RoundRobinStrategy(),
    registry:          agentRegistry,
    logger:            logger,
    maxRounds:         5,
    maxAgentsPerRound: 2);

var result = await orchestrator.RunTurnAsync(
    userMessage: new GroupMessage("user", "user", "What should we have for dinner?", DateTime.UtcNow),
    history:     [],
    agents:      [chefAgent, nutritionistAgent, budgetAgent],
    ctx:         AgentContext.For("conv-1", "msg-1", "What should we have for dinner?"),
    ct:          ct);

foreach (var msg in result.ProducedMessages)
    Console.WriteLine($"{msg.Sender}: {msg.Content}");
```

### Round Decision Strategies

**RoundRobinStrategy** — rotates through agents deterministically:

```csharp
// Round 1: agentA, Round 2: agentB, Round 3: agentA, ...
var strategy = new RoundRobinStrategy();
```

**PriorityRoundStrategy** — each agent speaks once in order:

```csharp
// Round 1: agents[0], Round 2: agents[1], ...stops when all have spoken
var strategy = new PriorityRoundStrategy();
```

**LlmRoundDecisionStrategy** — a fast LLM call decides who should speak:

```csharp
var strategy = new LlmRoundDecisionStrategy(
    llm:    gateway,
    model:  "deepseek-chat",
    logger: logger);
// The LLM receives agent descriptions + history and returns JSON:
// {"should_respond": ["agent-id-1"], "reason": "the user asked about nutrition"}
```

**ExpertRoutingStrategy** — routes to agents based on detected topic:

```csharp
var strategy = new ExpertRoutingStrategy(
    llm:    gateway,
    model:  "deepseek-chat",
    logger: logger);
// Detects the topic in the user message and picks the matching expert agent
```

**SentimentThresholdStrategy** — routes based on emotional state:

```csharp
var strategy = new SentimentThresholdStrategy(
    inner:          new RoundRobinStrategy(),  // fallback
    sadnessThresh:  0.7,                       // if sadness > 70%, route to empathy agent
    joyThresh:      0.8);                      // if joy > 80%, route to celebration agent
```

---

## Group Conversations

Group conversations support N humans + M agents in a shared space.

### Participants

```csharp
// Human participant (external user)
IParticipant human = new HumanParticipant("user-001", "Alice");

// Agent participant (wraps any IAgent)
IParticipant agentP = new AgentParticipant("naira-001", "Naira", nairaAgent);

// Check participant kind
if (participant.Kind == ParticipantKind.Human)
    Console.WriteLine("This is a human");
```

### ITurnPolicy

Decides which agents respond to a message:

```csharp
// All agents always respond
ITurnPolicy policy = new BroadcastTurnPolicy();

// Only the @mentioned agent responds
ITurnPolicy policy = new AddressedOnlyPolicy();

// Only humans can trigger agent responses (agents don't respond to each other)
ITurnPolicy policy = new HumanOnlyPassthroughPolicy();

// An LLM decides who should respond (like LlmRoundDecisionStrategy but for groups)
ITurnPolicy policy = new ModeratedTurnPolicy(gateway, moderatorModel: "deepseek-chat");
```

### IParticipantRouter

Decides message visibility:

```csharp
// Everyone sees everything
IParticipantRouter router = new BroadcastRouter();

// Only the addressed participant sees the message
IParticipantRouter router = new DirectMessageRouter();

// Escalates to human moderators when agents can't resolve
IParticipantRouter router = new EscalationRouter(humanModeratorId: "mod-001");

// Agents never see messages — human-only channel
IParticipantRouter router = new HumanOnlyRouter();
```

### IGroupConversationOrchestrator

Stateful orchestrator — manages participants and history across the conversation lifetime:

```csharp
// Build with DI
services.AddGroupConversations<BroadcastTurnPolicy, BroadcastRouter>(maxRoundsPerTurn: 3);

// Or directly
var orchestrator = new DefaultGroupConversationOrchestrator(
    turnPolicy:      new AddressedOnlyPolicy(),
    router:          new DirectMessageRouter(),
    logger:          logger,
    maxRoundsPerTurn: 5);

// Add participants
await orchestrator.AddParticipantAsync(new HumanParticipant("alice", "Alice"), ct);
await orchestrator.AddParticipantAsync(new AgentParticipant("naira", "Naira", nairaAgent), ct);
await orchestrator.AddParticipantAsync(new AgentParticipant("expert", "Expert", expertAgent), ct);

// Subscribe to events
orchestrator.OnMessageProduced  += async (_, e) => Console.WriteLine($"{e.Message.SenderName}: {e.Message.Content}");
orchestrator.OnParticipantJoined += async (_, e) => Console.WriteLine($"{e.Participant.DisplayName} joined");

// Send messages
var userMsg = GroupConversationMessage.FromUser("conv-001", "alice", "Alice", "Hello everyone!");
var result  = await orchestrator.SendMessageAsync(userMsg, ct);

Console.WriteLine($"Rounds: {result.RoundsExecuted}, Messages: {result.ProducedMessages.Count}");
```

Addressing a specific agent:

```csharp
// @Naira specifically
var msg = new GroupConversationMessage
{
    MessageId      = Guid.NewGuid().ToString(),
    ConversationId = "conv-001",
    SenderId       = "alice",
    SenderName     = "Alice",
    SenderKind     = ParticipantKind.Human,
    Content        = "Hey Naira, what do you think?",
    Role           = "user",
    Timestamp      = DateTime.UtcNow,
    AddressedToId  = "naira"  // only Naira will be asked to respond
};
```

---

## Composite Agents

A composite agent orchestrates sub-agents **privately** — the caller sees only the final response:

```csharp
[AgentCapability(Role = "Multi-stage analysis with self-critique")]
public class AnalysisWithCritiqueAgent : CompositeAgentBase<string>
{
    private readonly IAgent _analyst;
    private readonly IAgent _critic;

    public AnalysisWithCritiqueAgent(
        IAgent analyst,
        IAgent critic,
        ILogger<AgentBase<string>> logger,
        ILoggerFactory loggerFactory)
        : base(logger, loggerFactory)
    {
        _analyst = analyst;
        _critic  = critic;
    }

    public override string AgentId   => "analysis-with-critique";
    public override string AgentName => "Analysis + Critique";
    public override AgentRole Role   => AgentRole.Conversation;

    protected override bool DebugMode => false; // set true to replay private events

    public override IReadOnlyList<IAgent> SubAgents => [_analyst, _critic];

    // Step 1: run sub-agents internally
    protected override async Task OrchestrateSubAgentsAsync(
        AgentContext privateCtx, CancellationToken ct)
    {
        // First: analyst produces a draft
        var analysisResp = await _analyst.ProcessAsync(privateCtx, ct);

        // Second: critic reviews the draft
        var enrichedCtx = privateCtx with
        {
            UserMessage = $"Critique this analysis: {analysisResp.As<string>()}"
        };
        await _critic.ProcessAsync(enrichedCtx, ct);
    }

    // Step 2: merge results into the final response
    protected override Task<string?> BuildResponseAsync(
        AgentContext privateCtx,
        AgentContext parentCtx,
        CancellationToken ct)
    {
        var draft    = privateCtx.Results.LlmResponse ?? "";
        var critique = privateCtx.Results.Extra.GetValueOrDefault("critique")?.ToString() ?? "";

        var final = string.IsNullOrEmpty(critique)
            ? draft
            : $"{draft}\n\n[Reviewed]: {critique}";

        return Task.FromResult<string?>(final);
    }
}
```

**Internal debate pattern** (two agents argue, third decides):

```csharp
protected override async Task OrchestrateSubAgentsAsync(AgentContext ctx, CancellationToken ct)
{
    // Round 1: both sides make their case
    var proResponse  = await _proAgent.ProcessAsync(ctx, ct);
    var conResponse  = await _conAgent.ProcessAsync(ctx, ct);

    // Round 2: judge decides based on both arguments
    var judgeCtx = ctx with
    {
        UserMessage = $"Pro: {proResponse.As<string>()}\nCon: {conResponse.As<string>()}\nDecide."
    };
    await _judgeAgent.ProcessAsync(judgeCtx, ct);
}
```

**DebugMode** — replay private events to the caller's bus for admin inspection:

```csharp
protected override bool DebugMode => Environment.GetEnvironmentVariable("DEBUG_AGENTS") == "1";
```

---

## Dependency Injection

```csharp
var services = new ServiceCollection()
    .AddLogging()

    // Core infrastructure + agent discovery
    .AddMiyuAgents(
        Assembly.GetExecutingAssembly())

    // LLM gateway (choose one)
    .AddLoremGateway()             // development — no API key needed
    // .AddSingleton<ILlmGateway, DeepSeekGateway>()  // production

    // Group conversations with broadcast policy
    .AddGroupConversations<BroadcastTurnPolicy, BroadcastRouter>(maxRoundsPerTurn: 3)

    // Your concrete agents
    .AddSingleton<MathAgent>()
    .AddSingleton<HistoryAgent>()
    .BuildServiceProvider();
```

`[AgentCapability]` full attribute reference:

```csharp
[AgentCapability(
    Role               = "Answers questions about cooking",  // required — used for routing + debug
    MemoryAccess       = [MemoryKind.Episodic, MemoryKind.Declarative],  // what memory this agent uses
    CanInitiateLlmCalls = true,     // signals this agent makes LLM calls (for cost estimation)
    DependsOn          = [typeof(EmotionAnalysisAgent)],  // must run before this agent
    Lifetime           = AgentLifetime.Singleton          // Singleton | Scoped | Transient
)]
public class RecipeAgent : AgentBase<string> { ... }
```

`AddMiyuAgents` registers:
- `AgentRegistry` (discovers agents in provided assemblies via `[AgentCapability]`)
- `PipelineRunner`
- `ITurnOrchestrator` → `DefaultTurnOrchestrator`
- `IAgentEventBus` → `NullAgentEventBus`
- `IRealtimeBroadcaster` → `NullBroadcaster`
- All round-decision strategies (`LlmRoundDecisionStrategy`, `PriorityRoundStrategy`, `RoundRobinStrategy`)
- All turn policies (`BroadcastTurnPolicy`, `AddressedOnlyPolicy`)
- All routers (`BroadcastRouter`, `DirectMessageRouter`)

Replace defaults:

```csharp
// Replace the no-op event bus with your own (e.g., wrapping MediatR or an event store)
services.AddSingleton<IAgentEventBus, MyEventBus>();

// Replace the no-op broadcaster with SignalR
services.AddSingleton<IRealtimeBroadcaster, SignalRBroadcaster>();
```

---

## Examples

### Example 1: Simple single-agent pipeline

```csharp
var gateway  = new LoremGateway();
var agent    = new GreetingAgent(logger);
var pipeline = new PipelineRunner([new LlmCallStage(agent, gateway)]);

var ctx    = AgentContext.For("c1", "m1", "Tell me a joke");
var result = await pipeline.RunAsync(ctx, new PipelineContext(), ct);
Console.WriteLine(ctx.Results.LlmResponse);
```

### Example 2: Multi-stage pipeline with memory

```csharp
var pipeline = new PipelineRunner(
[
    // Priority 300: analyze emotion before answering
    new ConditionalStage(
        ctx => ctx.History.Count > 0,
        new EmotionAnalysisStage(emotionAnalyzer)),

    // Priority 200: retrieve relevant memories
    new TimedStage(
        new MemoryRetrievalStage(memoryAgent),
        timeout: TimeSpan.FromSeconds(1.5)),

    // Priority 600: LLM call
    new RetryStage(new LlmCallStage(gateway), maxAttempts: 2),

    // Priority 800: abort if nothing was produced
    new AbortIfEmptyStage("response", ctx => ctx.Results.LlmResponse),

    // Priority 900: async consolidation (fire-and-forget)
    new BackgroundConsolidationStage(consolidationChain),
]);
```

### Example 3: Group conversation with 3 agents

```csharp
// Three specialist agents
var chefAgent        = new ChefAgent(gateway, logger);
var nutritionAgent   = new NutritionAgent(gateway, logger);
var budgetAgent      = new BudgetAgent(gateway, logger);

var orchestrator = new DefaultGroupConversationOrchestrator(
    turnPolicy:       new BroadcastTurnPolicy(),
    router:           new BroadcastRouter(),
    logger:           logger,
    maxRoundsPerTurn: 1);  // each responds once per user message

await orchestrator.AddParticipantAsync(new HumanParticipant("user-1", "Maria"), ct);
await orchestrator.AddParticipantAsync(new AgentParticipant("chef",   "Chef Claude",    chefAgent),      ct);
await orchestrator.AddParticipantAsync(new AgentParticipant("nutri",  "Dr. Nutrition",  nutritionAgent), ct);
await orchestrator.AddParticipantAsync(new AgentParticipant("budget", "Budget Advisor", budgetAgent),    ct);

orchestrator.OnMessageProduced += async (_, e) =>
    Console.WriteLine($"  [{e.Message.SenderName}]: {e.Message.Content}");

var msg    = GroupConversationMessage.FromUser("conv-1", "user-1", "Maria", "What should I cook tonight?");
var result = await orchestrator.SendMessageAsync(msg, ct);
// Chef, Nutritionist, and Budget Advisor all respond
```

### Example 4: Addressed conversation (@ mentions)

```csharp
var orchestrator = new DefaultGroupConversationOrchestrator(
    turnPolicy: new AddressedOnlyPolicy(),
    router:     new DirectMessageRouter(),
    logger:     logger);

await orchestrator.AddParticipantAsync(new AgentParticipant("legal",  "Legal Bot",    legalAgent),  ct);
await orchestrator.AddParticipantAsync(new AgentParticipant("tech",   "Tech Bot",     techAgent),   ct);

// Only the legal agent responds
var msg = new GroupConversationMessage
{
    ConversationId = "conv-1",
    MessageId      = Guid.NewGuid().ToString(),
    SenderId       = "user-1",
    SenderName     = "Bob",
    SenderKind     = ParticipantKind.Human,
    Content        = "Is this contract clause valid?",
    Role           = "user",
    Timestamp      = DateTime.UtcNow,
    AddressedToId  = "legal"
};
await orchestrator.SendMessageAsync(msg, ct);
```

### Example 5: Composite agent with internal debate

```csharp
public class DebateAgent : CompositeAgentBase<string>
{
    // Pro argues FOR the user's idea
    // Con argues AGAINST it
    // Judge makes the final call
    private readonly IAgent _pro, _con, _judge;

    public override string AgentId   => "debate";
    public override string AgentName => "Debate Agent";
    public override AgentRole Role   => AgentRole.Conversation;
    protected override bool DebugMode => false;
    public override IReadOnlyList<IAgent> SubAgents => [_pro, _con, _judge];

    protected override async Task OrchestrateSubAgentsAsync(AgentContext ctx, CancellationToken ct)
    {
        var proCtx = ctx with { UserMessage = $"Argue FOR this idea: {ctx.UserMessage}" };
        var conCtx = ctx with { UserMessage = $"Argue AGAINST this idea: {ctx.UserMessage}" };

        var proResp = await _pro.ProcessAsync(proCtx, ct);
        var conResp = await _con.ProcessAsync(conCtx, ct);

        var judgeCtx = ctx with
        {
            UserMessage = $"""
                PRO: {proResp.As<string>()}
                CON: {conResp.As<string>()}
                Given these arguments, what is your balanced verdict?
                """
        };
        await _judge.ProcessAsync(judgeCtx, ct);
    }

    protected override Task<string?> BuildResponseAsync(
        AgentContext privateCtx, AgentContext parentCtx, CancellationToken ct)
        => Task.FromResult(privateCtx.Results.LlmResponse);
}
```

### Example 6: Multi-model gateway routing

```csharp
// Each gateway advertises which models it handles via SupportedModels
// Router: exact match → partial provider name match → first registered
var router = new LlmGatewayRouter([deepSeekGateway, anthropicGateway, loremGateway]);

// Usage is transparent — just set the model in LlmRequest
var req = new LlmRequest { Model = "claude-sonnet-4-6", Messages = [...] };

// Resolves to anthropicGateway (declared "claude-sonnet-4-6" in SupportedModels)
var gateway = router.Resolve(req.Model);
var res     = await gateway.CompleteAsync(req, ct);

// Aggregate stats from all providers
var stats = router.AggregatedStats();
```

### Example 7: Memory with LTP decay across turns

```csharp
var window = new MemoryWindow<string>(defaultTurns: 3);

// Turn 1: user mentions they like jazz — store it in working memory
window.UpdateWith([("pref-jazz", "user likes jazz music")]);
// → (New: 1, Refreshed: 0)

// After each turn: tick the decay clock
window.ApplyDecay();  // TurnsRemaining: 3 → 2 → 1 → 0 (evicted)

// Turn 2: memory retrieval returns the same entry — reconsolidation resets counter
if (userMessage.Contains("music"))
{
    window.UpdateWith([("pref-jazz", "user likes jazz music")]);
    // TurnsRemaining reset to 3 (Refreshed: 1)
}

// Inject active entries into LLM prompt
var context = string.Join("\n", window.ActiveEntries.Select(e => $"- {e}"));
```

### Example 8: AgentRegistry discovery via attributes

```csharp
// Annotate agents for automatic discovery
[AgentCapability(Role = "Answers questions about cooking recipes")]
public class RecipeAgent : AgentBase<string> { ... }

[AgentCapability(Role = "Provides nutritional information for foods")]
public class NutritionAgent : AgentBase<string> { ... }

// At startup — discovers all annotated agents in the assembly
var registry = new AgentRegistry();
registry.RegisterFromAssembly(Assembly.GetExecutingAssembly(), services);

// The ExpertRoutingStrategy uses these descriptions to route questions
var strategy = new ExpertRoutingStrategy(gateway, "deepseek-chat", logger);
// "How many calories in pasta?" → routes to NutritionAgent
// "How do I make carbonara?" → routes to RecipeAgent
```

---

## Testing

The framework ships with two test projects covering all public surface area:

```
tests/
├── MiyuAgents.Tests.Unit/         # 262 tests — isolated, no I/O, fast
└── MiyuAgents.Tests.Integration/  # 71 tests — real DI, real implementations
```

```bash
# Run all tests
dotnet test src/MiyuAgents.sln

# Run unit tests only
dotnet test tests/MiyuAgents.Tests.Unit

# Run integration tests only
dotnet test tests/MiyuAgents.Tests.Integration
```

**Unit test coverage** — every public type has a dedicated test class. Patterns used:
- `IAsyncLifetime.InitializeAsync` for arrange + act; `[Fact]` per assert
- `[Theory] + [MemberData]` for parameterized scenarios
- `FakeAgent`, `FakePipelineStage`, `FakeGateway` fakes in `Helpers/` — no mocking framework needed

**Integration test coverage** — exercises real component interactions:
- DI wiring via `AddMiyuAgents()` / `AddGroupConversations()`
- `PipelineRunner` end-to-end with real stages and agents
- `DefaultGroupOrchestrator` with `PriorityRoundStrategy` and `RoundRobinStrategy`
- `DefaultGroupConversationOrchestrator` with `BroadcastTurnPolicy` and `AddressedOnlyPolicy`
- `CompositeAgentBase` private context isolation and DebugMode event replay
- `LoremGateway` complete / stream / embed

---

## Priority Reference

| Range     | Tier             | Examples                                       |
|-----------|------------------|------------------------------------------------|
| 100–199   | Input validation | message guard, content filtering               |
| 200–299   | Retrieval        | memory retrieval, knowledge base search        |
| 300–399   | Analysis         | emotion analysis, concept extraction           |
| 400–499   | Context build    | prompt assembly, history truncation            |
| 500–599   | Tool use         | function calling, code execution               |
| 600–699   | LLM call         | primary completion, streaming                  |
| 700–799   | Post-analysis    | response emotion, fact extraction              |
| 800–899   | Delivery         | broadcast to client, event emission            |
| 900+      | Background       | consolidation, token tracking, indexing        |

---

## Project Structure

```
MiyuAgents/
├── Core/
│   ├── IAgent.cs                    # Main agent interface + lifecycle events
│   ├── AgentBase.cs                 # Template Method base class
│   ├── AgentContext.cs              # Immutable turn context (sealed record)
│   ├── AgentContextAccumulator.cs   # Mutable results accumulator
│   ├── AgentResponse.cs             # Response wrapper (Ok/Empty/Error/Degraded)
│   ├── AgentRegistry.cs             # Assembly-scan agent discovery
│   ├── CompositeAgentBase.cs        # Base for private sub-agent orchestration
│   ├── DefaultPrivateOrchestrator.cs # Internal round loop for composite agents
│   ├── InMemoryAgentEventBus.cs     # Captures events for replay (DebugMode)
│   ├── PrivateAgentContext.cs       # Creates isolated context for sub-agents
│   ├── IConversationAgent.cs        # Streaming conversation extension
│   ├── IVisionAgent.cs              # Image description extension
│   └── IAnalysisAgent.cs            # Structured analysis extension
├── Memory/
│   ├── IMemoryAgent.cs              # Memory agent interface
│   ├── MemoryAgentBase.cs           # Template Method for memory agents
│   ├── IMemoryStore.cs              # Generic store interface
│   ├── MemoryWindow.cs              # LTP decay working memory
│   ├── ConsolidationChain.cs        # Chain of Responsibility for post-turn storage
│   ├── InMemoryStore.cs             # In-memory store (testing / development)
│   └── CachingMemoryStore.cs        # LRU cache decorator
├── Llm/
│   ├── ILlmGateway.cs               # Unified LLM interface
│   ├── GatewayBase.cs               # Template Method with retry + stats
│   ├── LlmGatewayRouter.cs          # Multi-model routing
│   ├── LlmGatewayStats.cs           # Thread-safe stats tracker
│   ├── LoremGateway.cs              # No-API-key development fallback
│   └── TokenTracker.cs              # Per-conversation token accounting
├── Pipeline/
│   ├── IPipelineStage.cs            # Stage interface + result types
│   ├── PipelineRunner.cs            # Ordered stage execution
│   ├── PipelineContext.cs           # Carries event bus + broadcaster
│   ├── ParallelAgentStage.cs        # WhenAll wrapper for parallel agents
│   ├── AbortIfEmptyStage.cs         # Guard stage for required fields
│   ├── ConditionalStage.cs          # Conditional stage wrapper
│   ├── RetryStage.cs                # Exponential backoff retry wrapper
│   ├── TimedStage.cs                # Timeout wrapper (clean abort)
│   ├── IAgentEventBus.cs            # Event publication interface
│   ├── NullAgentEventBus.cs         # No-op event bus
│   ├── ConsoleAgentEventBus.cs      # Console-printing event bus (dev)
│   ├── IRealTimeBroadcaster.cs      # Client push interface (SignalR etc.)
│   └── NullBroadcaster.cs           # No-op broadcaster
├── Orchestration/
│   ├── ITurnOrchestrator.cs         # Single-user turn interface
│   ├── DefaultTurnOrchestrator.cs   # Wraps PipelineRunner
│   ├── IGroupOrchestrator.cs        # Stateless multi-agent turn interface
│   ├── DefaultGroupOrchestrator.cs  # Strategy-based multi-agent loop
│   └── Strategies/
│       ├── IRoundDecisionStrategy.cs
│       ├── LlmRoundDecisionStrategy.cs
│       ├── PriorityRoundStrategy.cs
│       ├── RoundRobinStrategy.cs
│       ├── ExpertRoutingStrategy.cs
│       └── SentimentThresholdStrategy.cs
├── GroupConversations/
│   ├── IParticipant.cs              # Human + Agent participant types
│   ├── GroupConversationMessage.cs  # Rich group message
│   ├── GroupConversationContext.cs  # Turn context with group state
│   ├── ITurnPolicy.cs               # Who responds? (4 implementations)
│   ├── IParticipantRouter.cs        # Who sees what? (4 implementations)
│   ├── IGroupConversationOrchestrator.cs  # Stateful N×M orchestrator
│   └── DefaultGroupConversationOrchestrator.cs
└── Extensions/
    └── ServiceCollectionExtensions.cs  # AddMiyuAgents() + AddGroupConversations()
```
