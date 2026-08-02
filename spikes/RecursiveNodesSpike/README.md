# Recursive nodes spike — archived design record

This directory contains the original executable proof of concept that explored functional recursion
over MiyuAgents workflow primitives. The experiment has been promoted. The current production API is
`src/Workflows/RecursiveWorkflowNode.cs`; managed interactive execution is implemented in
`src/Workflows/WorkflowRun.cs`.

For current usage and limitations, read [`../../docs/workflows.md`](../../docs/workflows.md). This
file records what the spike proved and should not be treated as the API reference.

## Question explored

MiyuAgents already supported structural composition: a `WorkflowNode` could contain another
`WorkflowNode`. The spike asked whether a node could also express functional self-recursion, where a
frame schedules a smaller frame and optionally folds the child result while returning.

The proof of concept evaluated:

- deferred self-reference;
- explicit base cases;
- depth limits and stack safety;
- active-chain cycle detection;
- context propagation;
- tail-recursive traversal.

## What was promoted

The production `RecursiveWorkflowNode<TState>` is substantially more capable than the original PoC:

- generic strongly typed state;
- asynchronous bodies;
- an explicit trampoline, so CLR stack depth does not grow;
- optional async return continuations for non-tail folds;
- independent depth, call-count, and wall-clock budgets;
- per-run active-cycle detection with an optional domain cycle key;
- cancellation and steering at every frame;
- hierarchical trace lanes through `INodeTraceSink`;
- preservation of `NodeState`, `AgentContext`, and attachments;
- safe concurrent reuse of one node instance.

`WorkflowBuilder` also rejects structural `NodeSpec` cycles and directs intentional self-recursion to
the functional recursion primitive.

## Current production shape

```csharp
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

`Done` above is an application helper that constructs a successful `NodeResult`.

## Important boundary discovered later

The primitive schedules exactly one smaller recursive call per frame. It naturally represents a
chain and its unwind, not a dynamic fan-out tree. A runtime-discovered branching tree still needs an
explicit worklist or a custom dynamic fork/join abstraction.

Fixed collections are not a recursion use case:

- use `ParallelStrategy` for independent files or images;
- use `SequenceStrategy` for ordered stages;
- use `LoopStrategy` for repeated tool/model work;
- use `RecursiveWorkflowNode<TState>` when a frame discovers additional depth while running.

## Historical PoC

The original console program remains in this directory for provenance. It covers recursive sum,
depth bounds, cycle detection, workflow-data traversal, and tail-recursive Fibonacci. Its internal
types predate the promoted API and are intentionally isolated from `src`.

Run it from the repository root:

```powershell
dotnet run --project .\MiyuAgents\spikes\RecursiveNodesSpike\RecursiveNodesSpike.csproj
```

Successful output ends with `RECURSIVE_SPIKE_OK`.
