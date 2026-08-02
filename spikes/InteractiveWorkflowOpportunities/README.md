# Interactive workflow opportunities spike

This executable spike evaluates the controllable and recursive MiyuAgents runtime against real
workflow shapes found in Angel Naira. It does not call external services and does not modify the
vendored package inside the application.

The authoritative framework reference is [`../../docs/workflows.md`](../../docs/workflows.md).

## Executive result

The highest-value change is not making every workflow recursive. It is giving every long-running
execution a consistent lifecycle: cooperative stop, steering at safe checkpoints, FIFO follow-ups,
visible input waits, multimodal context preservation, inherited history, and a semantic handoff.

Functional recursion adds value only when a frame discovers additional depth while running.

| Area | Stop / steer / queue | Functional recursion | Priority | Recommended shape |
|---|---:|---:|---:|---|
| Writing and theory teams | Very high | Medium | P0 | Resumable session + driver + managed run |
| Kerberos code loop | Very high | Low unless tasks form a dynamic tree | P0 | Checkpoints between LLM/tool calls; keep the loop as a leaf |
| Long-form reading | High | Low | P0/P1 | Keep `LoopStrategy`; unify run ownership |
| Multi-image/Gasnyaphh batch | Medium | Low | P1 | Parallel fan-out + aggregation + one upload |
| Photo prompt workflow | Medium | Low | P1 | Keep critique and sanitizer loops |
| Autonomous dreams/groups | Selective | Low | P2 | Domain adapters; do not force human steering |

## Scenarios exercised by the executable

### Clarification, resume, steering, and queued handoff

The spike models a writing team as two nodes:

1. `team-start` creates a session and returns `NeedsInput`.
2. The driver waits asynchronously while the managed run reports `PendingInput`.
3. A steering message is injected before resume.
4. `team-resume` receives the session, answer, and steering context.
5. A handoff subnode reads prior root results and the internal transcript.
6. A FIFO follow-up starts a fresh control loop with inherited semantic context.

This is the shape needed to enable clarification in domain teams without deadlocking the UI.

### Recursive descent and unwind

The recursion scenario descends through runtime-discovered outline depth and composes the result while
the continuation stack unwinds. This demonstrates the valid use of `RecursiveWorkflowNode<TState>`:
one frame discovers one smaller frame.

### Fixed multi-image batch

Three image jobs run in parallel, their artifacts fan in, and one final upload node runs afterward.
This intentionally demonstrates that a known batch is a `ParallelStrategy` problem, not a recursion
problem. The workflow improves progress, cancellation, and latency around Gasnyaphh; it does not
change the external service itself.

### Cooperative stop

The final scenario verifies that a managed recursive run observes cancellation and reaches the
`Canceled` status.

## Runtime findings promoted from the spike

### Current-pass progress and inherited context are separate

History inheritance is intentional. `NodeState.History` now represents only current-pass control
progress. Completed root results move to `PriorHistory`, and their bounded internal events move to
`PriorTranscript`. This prevents strategies from treating old context as current execution while
preserving everything a subnode needs to understand the follow-up.

### Semantic handoff subnode

`WorkflowRunOptions.HandoffNode` receives prior history, prior transcript, the queued request, and
new attachments. It returns `WorkflowHandoff(Summary, Reason)`. The handoff is structured for rich
nodes and also rendered into `EffectiveInput` for ordinary agents.

### Bounded internal transcript

Each composite `WorkflowNode` result includes child results, retries, driver questions/answers,
signals, hierarchical lanes, and bounded artifact previews. Nested transcripts bubble to parents.
Heavy payloads are not retained.

## Angel Naira opportunities

### P0: writing and theory teams

The domain orchestrator already supports `NeedsClarification` and `ResumeAsync`, but the built-in
writing/theory configurations disable clarification. Add a start/resume adapter, a driver, managed
run ownership, and chat events for `PendingInput`.

### P0: code loop

Connect stop, steering, and queued input to safe points between model and tool operations. Do not
rewrite the established ReAct loop as functional recursion. Use recursion only if planning creates a
runtime-discovered task hierarchy.

### P0/P1: long-form reading

Keep the existing passage loop. Replace duplicate run ownership with the common managed-run contract,
and accept steering between passages.

### P1: multi-image work

Use parallel per-image processing, progress per branch, aggregation, and one final upload. Functional
recursion is warranted only if processing discovers unknown nested albums, folders, or derived tasks.

## Remaining production gaps

1. `WorkflowRunHandle` exposes `PendingInput`, but answers still enter through a concrete `IDriver`.
   A durable host normally needs a typed `Answer(runId, text, attachments)` inbox.
2. `WorkflowRunManager` is in memory. Refresh/restart recovery and horizontal scaling require a run
   store, event persistence, and leases or another ownership mechanism.
3. Functional recursion schedules one smaller call per frame. Dynamic branching needs an explicit
   worklist or a future dynamic fork/join primitive.
4. Cancellation is cooperative. A child that ignores its token can continue external side effects.
5. Each integration must define safe steering checkpoints.
6. Follow-up queues still need host-level size limits, priorities, deduplication, and durable storage
   when unbounded in-memory queues are unacceptable.
7. The internal transcript travels between passes, but the host must persist it when runs must survive
   process boundaries.

## Run the spike

From the repository root:

```powershell
dotnet run --project .\MiyuAgents\spikes\InteractiveWorkflowOpportunities\InteractiveWorkflowOpportunities.csproj
```

Successful output ends with:

```text
INTERACTIVE_OPPORTUNITIES_SPIKE_OK
```
