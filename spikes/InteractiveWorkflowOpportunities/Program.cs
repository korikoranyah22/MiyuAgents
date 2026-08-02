using Microsoft.Extensions.Logging.Abstractions;
using MiyuAgents.Core;
using MiyuAgents.Workflows;
using System.Collections.Concurrent;

await ClarificationResumeAndQueueAsync();
await RecursiveDescentAndUnwindAsync();
await ParallelImageBatchAsync();
await CooperativeStopAsync();

Console.WriteLine("INTERACTIVE_OPPORTUNITIES_SPIKE_OK");

static async Task ClarificationResumeAndQueueAsync()
{
    var driver = new InteractiveDriver();
    var sessions = 0;
    var outputs = new ConcurrentQueue<string>();
    var handoffs = new ConcurrentQueue<WorkflowHandoff>();

    var start = new DelegateNode("team-start", (state, _) =>
    {
        if (state.Handoff is { } handoff) handoffs.Enqueue(handoff);
        var sessionId = $"writing-{Interlocked.Increment(ref sessions)}";
        return Task.FromResult(Result(
            "team-start",
            NodeSignal.NeedsInput,
            artifacts: [new Artifact("session", "deliberation-session", sessionId)],
            ask: $"¿Qué tesis querés privilegiar para '{state.Input}'?"));
    });

    var resume = new DelegateNode("team-resume", (state, _) =>
    {
        var sessionId = state.History
            .SelectMany(x => x.Artifacts)
            .Last(x => x.Kind == "session").Payload?.ToString();
        var answer = state.History
            .Last(x => x.Response.AgentId == "driver").Response.Data?.ToString();
        var steering = state.Messages.Select(x => x.Text).ToArray();
        var output = $"resume={sessionId}; answer={answer}; steer={string.Join(" | ", steering)}; input={state.Input}";
        outputs.Enqueue(output);
        return Task.FromResult(Result(
            "team-resume",
            artifacts: [new Artifact("text", "team-output", output)]));
    });

    var root = new WorkflowNode(
        "writing-team",
        new SequenceStrategy(["start", "resume"]),
        new Dictionary<string, IAgent> { ["start"] = start, ["resume"] = resume },
        NullLogger<AgentBase<NodeResult>>.Instance,
        driver: driver);
    var handoffNode = new DelegateNode("handoff-summarizer", (state, _) =>
    {
        var previous = state.PriorHistory.Last();
        var artifactNames = string.Join(", ", previous.Artifacts.Select(x => x.Name ?? x.Kind));
        var internalTrail = string.Join(" → ", state.PriorTranscript
            .Where(x => x.Kind != WorkflowTranscriptKind.Truncated)
            .Select(x => $"{x.NodeId}:{x.Signal}"));
        return Task.FromResult(Result(
            "handoff-summarizer",
            data: new WorkflowHandoff(
                Summary: $"Recorrido interno: {internalTrail}. Produjo: {artifactNames}.",
                Reason: $"Continúa porque el usuario pidió: {state.Context?.UserMessage ?? state.Input}.")));
    });
    var run = new WorkflowRunManager().Start(
        root,
        new NodeState { Input = "ensayo sobre identidad" },
        "clarification-resume",
        options: new WorkflowRunOptions { HandoffNode = handoffNode });

    var ask = await driver.Asked.Task.WaitAsync(TimeSpan.FromSeconds(2));
    Require(run.IsWaitingForInput && ask.Contains("tesis", StringComparison.OrdinalIgnoreCase),
        "the run must expose its clarification wait");
    Require(run.Steer("sumá un contrapunto materialista"), "steering must be accepted while waiting");
    Require(run.Enqueue("después prepará un abstract"), "a FIFO follow-up must be accepted");
    driver.Submit("la identidad como proceso relacional");

    await run.Completion.WaitAsync(TimeSpan.FromSeconds(2));

    Require(run.Status == WorkflowRunStatus.Completed, "the team run must complete");
    Require(outputs.Count == 2, "the queued follow-up must execute a fresh composite pass");
    Require(outputs.First().Contains("contrapunto materialista"), "steering must reach the resume phase");
    Require(handoffs.Single().Summary.Contains("team-output"), "the handoff must summarize previous artifacts");
    Require(handoffs.Single().Summary.Contains("resume:Done"), "the handoff must include the internal transcript");
    Require(handoffs.Single().Reason.Contains("abstract"), "the handoff must explain why work continues");
    Console.WriteLine("PASS clarification/resume + steer + summarized FIFO handoff");
}

static async Task RecursiveDescentAndUnwindAsync()
{
    string[] path = ["Ensayo", "Capítulo: identidad", "Sección: memoria", "Párrafo núcleo"];
    var node = new RecursiveWorkflowNode<int>(
        "outline-descent",
        _ => 0,
        (frame, _) =>
        {
            if (frame.State == path.Length - 1)
                return ValueTask.FromResult(RecursionStep<int>.Return(
                    Result("outline-descent", data: path[frame.State])));

            return ValueTask.FromResult(RecursionStep<int>.Next(
                frame.State + 1,
                (parent, child, _) => ValueTask.FromResult(Result(
                    "outline-descent",
                    data: $"{path[parent.State]} > {child.Response.Data}"))));
        },
        cycleKey: frame => frame.State.ToString());

    var result = await node.RunNodeAsync(new NodeState { Input = "expandí el esquema" });
    var rendered = result.Response.Data?.ToString() ?? "";

    Require(rendered == string.Join(" > ", path), "the continuation stack must unwind the hierarchy");
    Console.WriteLine("PASS recursive single-branch descent/unwind");
}

static async Task ParallelImageBatchAsync()
{
    var transforms = Enumerable.Range(1, 3)
        .ToDictionary(
            i => $"image-{i}",
            i => (IAgent)new DelegateNode($"image-{i}", async (_, ct) =>
            {
                await Task.Delay(20, ct);
                return Result($"image-{i}", artifacts: [new Artifact("image", $"image-{i}.webp", i)]);
            }));
    var batch = new WorkflowNode(
        "image-batch",
        new ParallelStrategy(transforms.Keys.ToArray()),
        transforms,
        NullLogger<AgentBase<NodeResult>>.Instance);
    var upload = new DelegateNode("upload", (state, _) =>
    {
        var images = state.History.SelectMany(x => x.Artifacts).Count(x => x.Kind == "image");
        return Task.FromResult(Result(
            "upload",
            data: images,
            artifacts: [new Artifact("upload", "gasnyaphh-batch", images)]));
    });
    var root = new WorkflowNode(
        "photo-batch",
        new SequenceStrategy(["transform", "upload"]),
        new Dictionary<string, IAgent> { ["transform"] = batch, ["upload"] = upload },
        NullLogger<AgentBase<NodeResult>>.Instance);

    var result = await root.RunNodeAsync(new NodeState { Input = "subí estas tres imágenes" });
    var uploaded = result.Artifacts.Last(x => x.Kind == "upload").Payload;

    Require(Equals(uploaded, 3), "all three images must fan in before upload");
    Console.WriteLine("PASS fixed image batch uses parallel fan-out, not recursion");
}

static async Task CooperativeStopAsync()
{
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var blocking = new RecursiveWorkflowNode<int>(
        "blocking-workflow",
        _ => 0,
        async (_, ct) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return RecursionStep<int>.Return(Result("blocking-workflow"));
        });
    var run = new WorkflowRunManager().Start(blocking, new NodeState { Input = "trabajá" }, "stop-proof");
    await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
    Require(run.Stop(), "stop must be accepted");
    try
    {
        await run.Completion;
        throw new InvalidOperationException("the canceled run unexpectedly completed");
    }
    catch (OperationCanceledException)
    {
        Require(run.Status == WorkflowRunStatus.Canceled, "the run must report canceled");
    }
    Console.WriteLine("PASS cooperative stop");
}

static NodeResult Result(
    string id,
    NodeSignal signal = NodeSignal.Done,
    object? data = null,
    IReadOnlyList<Artifact>? artifacts = null,
    string? ask = null) => new()
{
    Response = new AgentResponse
    {
        AgentId = id,
        AgentName = id,
        Role = AgentRole.Orchestration,
        Data = data,
    },
    Signal = signal,
    Artifacts = artifacts ?? [],
    Ask = ask,
};

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class InteractiveDriver : IDriver
{
    readonly TaskCompletionSource<string> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
    string? _submitted;

    public TaskCompletionSource<string> Asked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<string> AnswerAsync(string ask, NodeState state, CancellationToken ct = default)
    {
        Asked.TrySetResult(ask);
        if (_submitted is { } submitted) return submitted;
        return await _answer.Task.WaitAsync(ct);
    }

    public void Submit(string answer)
    {
        _submitted = answer;
        _answer.TrySetResult(answer);
    }
}

sealed class DelegateNode(
    string id,
    Func<NodeState, CancellationToken, Task<NodeResult>> run)
    : AgentBase<NodeResult>(NullLogger<AgentBase<NodeResult>>.Instance), INodeAgent
{
    public override string AgentId => id;
    public override string AgentName => id;
    public override AgentRole Role => AgentRole.Orchestration;

    public Task<NodeResult> RunNodeAsync(NodeState state, CancellationToken ct = default) => run(state, ct);

    protected override Task<NodeResult?> ExecuteCoreAsync(AgentContext ctx, CancellationToken ct)
        => RunNodeAsync(new NodeState { Input = ctx.UserMessage, Context = ctx }, ct)!;
}
