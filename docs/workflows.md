# Workflows

This document is the authoritative guide to the workflow runtime in MiyuAgents. It describes the
current API in `src/Workflows`, including managed runs, functional recursion, inherited context,
handoffs, internal transcripts, tracing, authoring, and known limitations.

## 1. Choose the smallest control primitive that fits

| Problem shape | Recommended primitive |
|---|---|
| A fixed ordered pipeline | `WorkflowNode` + `SequenceStrategy` |
| A fixed set of independent jobs | `WorkflowNode` + `ParallelStrategy` |
| Repeat one child until its signal changes | `WorkflowNode` + `LoopStrategy` |
| A fixed sequence of sequential/parallel phases | `DeliberateStrategy` |
| Plan, execute, and return to planning when execution is blocked | `PlanExecuteStrategy` |
| Bounded multi-agent turns with bids | `ConverseStrategy` or `FairConverseStrategy` |
| Statically nested sub-workflows | A `WorkflowNode` inside another `WorkflowNode` |
| Runtime-discovered single-branch depth with return-time folding | `RecursiveWorkflowNode<TState>` |
| A known list of files, images, or passages | Sequence, parallel, or loop; not functional recursion |
| Runtime-discovered branching task trees | An explicit worklist or custom fork/join node; no first-class dynamic fork/join exists yet |

Functional recursion is not a more powerful replacement for the control strategies. Use it only
when one frame discovers the next frame at runtime. A fixed three-image batch, for example, should
fan out with `ParallelStrategy`, aggregate artifacts, and upload once.

## 2. Mental model

`WorkflowNode` is both an `IAgent` and an `INodeAgent`. It owns a roster of child agents and runs a
control loop:

1. Ask the `IControlStrategy` for a `ControlDecision`.
2. Run the selected child or children.
3. Apply retry and signal handling.
4. Append settled results to current-pass `NodeState.History`.
5. Repeat until the strategy stops or the resilience budget is exhausted.

Because every `WorkflowNode` is an agent, a child can itself be another workflow. Nested execution
uses a shared immutable `NodeState`, hierarchical trace lanes, and the same cancellation token.

The dictionary key in the child roster is the control identity. The runtime re-stamps a child's
`AgentResponse.AgentId` with that roster key before adding it to `History`. This lets one agent
instance play multiple roles and keeps strategy matching stable.

## 3. Minimal workflow

The leaf variables below are ordinary `IAgent` implementations:

```csharp
var workflow = new WorkflowNode(
    id: "research-and-write",
    strategy: new SequenceStrategy(["research", "write"]),
    children: new Dictionary<string, IAgent>
    {
        ["research"] = researchAgent,
        ["write"] = writingAgent,
    },
    logger: logger,
    policy: new ResiliencePolicy(MaxSteps: 10, MaxRetries: 1));

var result = await workflow.RunNodeAsync(new NodeState
{
    Input = "Write a short brief about stack-safe recursion.",
    Context = turnContext,
}, ct);

if (result.Signal == NodeSignal.Done)
    foreach (var artifact in result.Artifacts)
        Console.WriteLine($"{artifact.Kind}: {artifact.Name}");
```

Use `RunNodeAsync` when the caller already speaks workflow primitives. Use `ProcessAsync` when the
workflow must be consumed as an ordinary `IAgent`; the runtime creates the initial `NodeState` from
the supplied `AgentContext`.

## 4. State and context

`NodeState` is immutable. Every control-loop step creates a new record value.

| Property | Meaning |
|---|---|
| `Input` | The request that started the current pass. |
| `Context` | The original `AgentContext`, including identity, conversation history, model, metadata, and media. May be null for direct node calls. |
| `Attachments` | Media for direct calls that do not have an `AgentContext`. |
| `Messages` | Live steering messages consumed at safe checkpoints during the current pass. |
| `History` | Child results settled in the current control-loop pass. Strategies use this as execution progress. |
| `PriorHistory` | Root results inherited from completed passes. This is semantic context, not current execution progress. |
| `PriorTranscript` | Bounded internal events inherited from completed passes. |
| `Handoff` | A compact summary plus the explicit reason the workflow is continuing. |
| `Round` | Current control-loop iteration. |
| `Bids` | Pending `RequestTurn` bids. |
| `EffectiveInput` | Handoff + current input + steering, formatted for a plain leaf agent. |
| `EffectiveAttachments` | Original media + direct media + media added through steering. |

`WorkflowNode` preserves the original context when it invokes a plain `IAgent`. It replaces only
`UserMessage` and `Attachments` with their effective values, so nested workflows do not silently
lose profile identity, conversation history, model selection, metadata, or multimodal input.

Do not use `History` as long-term conversation memory. It is local control state. Cross-pass
context belongs in `PriorHistory`, `PriorTranscript`, and `Handoff`; cross-conversation memory still
belongs in the host's memory system.

## 5. Results, artifacts, and signals

`NodeResult` wraps an `AgentResponse` and adds workflow-specific control data:

- `Signal`: how the parent should react.
- `Artifacts`: domain-neutral deliverables such as text, files, plans, diffs, images, or remote IDs.
- `Ask`: the question associated with `NeedsInput`.
- `Transcript`: a bounded internal execution transcript for composite workflows.

| Signal | Default parent behavior |
|---|---|
| `Done` | Record the result and continue according to the strategy. |
| `Continue` | Record the result; a `LoopStrategy` normally runs the same child again. |
| `NeedsInput` | Ask the configured `IDriver`, record the question and answer, then continue. |
| `NeedsReplanning` | Bubble to the parent unless an `ISignalReactiveStrategy` intercepts it. |
| `Failed` | Retry according to `MaxRetries`, then bubble unless intercepted. |
| `HandBack` | Bubble to the parent unless intercepted. |
| `RequestTurn` | Add a bid for a conversation strategy and continue. |

If no driver is configured, `NeedsInput` receives an empty answer. Workflows that genuinely require
clarification should always be built with an appropriate driver.

## 6. Built-in control strategies

### `SequenceStrategy`

Runs roster IDs in order, one per step, then stops. It determines progress by counting current-pass
history entries whose IDs belong to its ordered roster.

### `ParallelStrategy`

Runs all configured IDs with `Task.WhenAll` in one step and stops after any configured child appears
in current-pass history. `Task.WhenAll` preserves result ordering, but the child agents themselves
must be safe to run concurrently.

### `LoopStrategy`

Runs one child repeatedly. By default it repeats while the child's latest signal is `Continue` and
stops when that signal changes. `ResiliencePolicy.MaxSteps` is the hard anti-hang bound.

### `DeliberateStrategy`

Runs a fixed sequence of `Phase` objects. Each phase declares a roster and whether it runs
sequentially or in parallel. `NodeState.Round` is the phase cursor.

### `PlanExecuteStrategy`

Runs a planner, then an executor. When the executor emits `NeedsReplanning`, the strategy intercepts
the signal and returns to planning up to `maxReplans`; after that the signal bubbles out.

### `ConverseStrategy`

Runs a bounded conversation. Reactive `RequestTurn` bids take precedence when they do not select the
last speaker; optional `IBiddingParticipant` polling can request proactive bids; otherwise selection
falls back to round-robin.

### `FairConverseStrategy`

Adds participation fairness, recency windows, participant weights, bounded bid slack, optional
relevance polling, an opening speaker, and a decision callback. With polling enabled, it stops when
nobody wants the floor instead of manufacturing another round.

### Custom and signal-reactive strategies

Implement `IControlStrategy.NextAsync` for custom routing. Also implement
`ISignalReactiveStrategy.OnChildSignalAsync` when `Failed`, `NeedsReplanning`, or `HandBack` should
be intercepted. Returning null preserves the default bubble-up behavior; returning a terminal
decision stops; returning children re-routes the next step.

## 7. Drivers and clarification

An `IDriver` answers `NeedsInput` without coupling a workflow to a specific UI or caller:

| Driver | Intended use |
|---|---|
| `HumanDriver` | Wait asynchronously for a host/UI to call `Provide(promptId, answer)`. Exposes `OpenAsks` and `OnAsk`. |
| `CharacterDriver` | Ask another `IAgent`, allowing a character to converse with its workflow. |
| `SystemDriver` | Consume predefined answers and then use a non-blocking fallback for scheduled/headless runs. |

`HumanDriver` is in memory. Its open `TaskCompletionSource` objects do not survive a process restart;
the host must persist the prompt and restore or replace the run if durable clarification is needed.

During a managed run, `WorkflowRunHandle.PendingInput` and `IsWaitingForInput` expose the wait state.
The answer still enters through the configured driver; `WorkflowRunHandle` does not currently expose
a generic `Answer` method.

## 8. Managed interactive runs

`WorkflowRunManager` starts and tracks a controllable in-memory execution:

```csharp
var runs = new WorkflowRunManager();
var run = runs.Start(root, initialState, runId: "draft-42", ct: requestAborted);

run.Steer("Make the next revision more concise.");
run.Enqueue("After the draft, produce a five-line abstract.");

var result = await run.Completion;
```

The handle exposes status, timestamps, heartbeat, pending steering/follow-up counts, pending input,
the run cancellation token, and the completion task.

### Stop

`Stop()` requests cooperative cancellation and changes the status to `Stopping`. Framework waits are
cancellation-aware, so the managed completion is released even when a child ignores cancellation.
That does not kill the non-cooperative child: it may continue external side effects. Gateways,
processes, tools, and file writers must honor the token or provide their own kill/rollback semantics.

### Steer

`Steer` injects a user message into the active pass at the next safe checkpoint. `WorkflowNode`
checks once per control-loop step; `RecursiveWorkflowNode<TState>` checks once per frame. A custom
long-running node should call:

```csharp
state = WorkflowRunScope.Current?.Checkpoint(state) ?? state;
```

Place checkpoints between semantic operations, never halfway through an irreversible tool call.
Steering received after the final checkpoint becomes the next pass rather than disappearing.

### Enqueue

`Enqueue` adds a FIFO follow-up pass. Follow-ups are not collapsed. The next pass starts with empty
current `History`, while completed root results and bounded internal events move to `PriorHistory`
and `PriorTranscript`.

Posting at the completion boundary is atomic: a message either extends the run or observes a terminal
status and is rejected.

## 9. Handoffs between passes

`WorkflowRunOptions.HandoffNode` is an optional subnode invoked before a queued pass. It receives:

- the new request in `Input`/`Context`;
- completed root results in `PriorHistory`;
- internal execution events in `PriorTranscript`;
- new attachments.

It may return a structured `WorkflowHandoff` in `NodeResult.Response.Data`:

```csharp
var run = runs.Start(root, initialState, options: new WorkflowRunOptions
{
    HandoffNode = handoffSummarizer,
    MaxPriorTranscriptEntries = 400,
});

// Expected handoff result:
NodeResult.From(response with
{
    Data = new WorkflowHandoff(
        Summary: "The research and critique phases completed; the reviewer requested two changes.",
        Reason: "The user queued a shorter executive version.")
});
```

A plain string response is accepted as the summary and keeps the deterministic reason. When no
handoff node is configured, or the node fails, the runtime builds a fallback from the previous
signal, internal step count, artifact names, and queued request.

The structured handoff is also rendered into `EffectiveInput`, so ordinary `IAgent` children receive
the context even if they do not understand `NodeState`.

## 10. Internal transcripts and live tracing

These are related but distinct:

| Mechanism | Purpose | Lifetime |
|---|---|---|
| `NodeResult.Transcript` | Compact semantic context for parents and queued passes. | Travels with the result; bounded in memory. |
| `INodeTraceSink` / `NodeTrace` | Live diagnostics, UI streaming, and host-side event persistence. | Defined by the host sink. |

Each composite `WorkflowNode` transcript can contain:

- final child results and retry attempts;
- signals and hierarchical lanes;
- driver questions and answers;
- artifact kind/name/id and bounded payload previews;
- a truncation marker when older entries were dropped.

Nested workflow transcripts bubble to their parent. Raw images, byte arrays, and arbitrary object
graphs are never retained in transcript entries; only a type marker is kept. String and primitive
previews are bounded by `ResiliencePolicy.MaxTranscriptTextLength`.

Defaults are 200 entries and 1,000 characters per entry for each `WorkflowNode`, and 400 inherited
entries across managed passes. Configure these with `ResiliencePolicy` and `WorkflowRunOptions`.

`RecursiveWorkflowNode<TState>` emits nested frame lanes to `INodeTraceSink`, but it does not
automatically synthesize a `NodeResult.Transcript` for every frame. Its body may return transcript
entries explicitly when semantic frame history is required by a handoff.

Use live tracing like this:

```csharp
using (NodeTrace.Begin(traceSink))
using (NodePlugins.Begin(sandboxPlugin))
{
    var result = await root.RunNodeAsync(initialState, ct);
}
```

`NodeScope` and `NodeTrace` use `AsyncLocal`, so parallel branches retain isolated hierarchical lane
paths. `NodePlugins` uses the same ambient pattern for run-scoped services such as `ISandboxPort`.

## 11. Functional recursion

`RecursiveWorkflowNode<TState>` provides async, stack-safe functional recursion with an explicit
trampoline and continuation stack:

- `RecursionStep<TState>.Return(result)` completes the base case.
- `RecursionStep<TState>.Next(nextState)` performs tail recursion.
- `Next(nextState, onReturn)` adds asynchronous return-time folding for non-tail recursion.
- `RecursionPolicy` bounds depth, total calls, and wall-clock duration.
- Optional cycle keys detect repeated active states before budgets are exhausted.
- Cancellation and steering are checked for every frame.

```csharp
static NodeResult Done(int value) => NodeResult.From(new AgentResponse
{
    AgentId = "factorial",
    AgentName = "Factorial",
    Role = AgentRole.Orchestration,
    Data = value,
});

var factorial = new RecursiveWorkflowNode<int>(
    id: "factorial",
    seed: state => int.Parse(state.Input),
    body: (frame, ct) => ValueTask.FromResult(
        frame.State <= 1
            ? RecursionStep<int>.Return(Done(1))
            : RecursionStep<int>.Next(
                frame.State - 1,
                (parent, child, ct) => ValueTask.FromResult(
                    Done(parent.State * (int)child.Response.Data!)))),
    policy: new RecursionPolicy
    {
        MaxDepth = 32,
        MaxCalls = 64,
        MaxDuration = TimeSpan.FromSeconds(30),
    },
    cycleKey: frame => frame.State.ToString());
```

The current recursion step schedules exactly one smaller recursive call. A branching tree therefore
needs an explicit worklist or a custom dynamic fork/join primitive. Do not disguise a fixed loop or
fixed parallel batch as recursion.

`WorkflowBuilder` rejects structural `NodeSpec` reference cycles. Intentional self-recursion must be
implemented by a registered `RecursiveWorkflowNode<TState>` leaf.

## 12. Workflows as data

`WorkflowSpec` and `NodeSpec` describe a static workflow tree. `WorkflowBuilder` resolves leaf IDs
through an `IWorkflowRegistry`, constructs nested nodes, applies per-node budgets, and rejects:

- structural reference cycles;
- duplicate child IDs under one node;
- missing registered agents;
- unknown strategy names.

The default `WorkflowRegistry` supports `sequence`, `parallel`, `loop`, `converse`, and
`plan-execute`. Register extra factories for `deliberate`, `fair-converse`, or domain strategies.

```csharp
var spec = new WorkflowSpec(
    Id: "review",
    DisplayName: "Review",
    Description: "Draft and review a document",
    Root: new NodeSpec(
        Id: "root",
        Strategy: "sequence",
        Members: ["draft", "reviewer"]));

var root = WorkflowBuilder.Build(spec, registry, logger);
```

`IWorkflowStore` is a synchronous CRUD abstraction for specs. `InMemoryWorkflowStore` is suitable for
tests and hot authoring inside one process; persistence and versioning belong to the host.

## 13. Resilience, concurrency, and ownership

`ResiliencePolicy` controls:

- `MaxSteps`: hard bound for one composite control loop;
- `MaxRetries`: retries for a failed child before routing/bubble-up;
- `MaxTranscriptEntries`: internal events retained by the node result;
- `MaxTranscriptTextLength`: maximum preview length.

`WorkflowNode` and `RecursiveWorkflowNode<TState>` keep mutable execution data inside each call and
are reentrant. Reusing the same node concurrently is safe only when every leaf agent, driver,
strategy dependency, and plugin it references is also thread-safe.

`WorkflowRunManager` is an in-memory owner registry, not a durable scheduler. It does not provide:

- recovery after process restart;
- cross-instance ownership or leases;
- a persistent message inbox;
- queue limits, priorities, or deduplication;
- a generic answer channel for `PendingInput`;
- hard termination of non-cooperative external work.

Hosts that need those guarantees should persist run events and transcripts, assign ownership, and
reconstruct or compensate work through host-specific infrastructure.

## 14. Integration checklist

Before exposing a workflow through a UI or API:

1. Choose a strategy based on the actual control shape.
2. Set explicit step/retry/transcript budgets.
3. Pass one cancellation token through every gateway and tool.
4. Declare safe steering checkpoints.
5. Choose a driver for every possible `NeedsInput` path.
6. Decide whether queued passes need a handoff summarizer.
7. Preserve `AgentContext` and attachments at the root.
8. Attach an `INodeTraceSink` when live progress or durable events are required.
9. Ensure parallel children and shared plugins are thread-safe.
10. Persist runs outside `WorkflowRunManager` if refresh, restart, or horizontal scale matters.

## 15. Executable coverage

The workflow documentation is exercised by the unit and integration suites, especially:

- `tests/MiyuAgents.Tests.Unit/Workflows/WorkflowNodeTests.cs`
- `tests/MiyuAgents.Tests.Unit/Workflows/WorkflowStrategiesTests.cs`
- `tests/MiyuAgents.Tests.Unit/Workflows/RecursiveWorkflowNodeTests.cs`
- `tests/MiyuAgents.Tests.Unit/Workflows/WorkflowAuthoringTests.cs`
- `tests/MiyuAgents.Tests.Unit/Workflows/TraceTests.cs`
- `tests/MiyuAgents.Tests.Integration/`
- `spikes/InteractiveWorkflowOpportunities/`

Run the complete verification with:

```powershell
dotnet test .\tests\MiyuAgents.Tests.Unit\MiyuAgents.Tests.Unit.csproj
dotnet test .\tests\MiyuAgents.Tests.Integration\MiyuAgents.Tests.Integration.csproj
dotnet run --project .\spikes\InteractiveWorkflowOpportunities\InteractiveWorkflowOpportunities.csproj
```
