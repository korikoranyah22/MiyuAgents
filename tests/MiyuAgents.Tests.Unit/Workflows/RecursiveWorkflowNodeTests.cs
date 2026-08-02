using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MiyuAgents.Core;
using MiyuAgents.Workflows;
using Xunit;

namespace MiyuAgents.Tests.Unit.Workflows;

public class RecursiveWorkflowNodeTests
{
    [Fact]
    public async Task TailRecursion_IsAsyncAndStackSafe()
    {
        var node = new RecursiveWorkflowNode<(int N, int Sum)>(
            "sum",
            state => (int.Parse(state.Input), 0),
            async (frame, ct) =>
            {
                await Task.Yield();
                return frame.State.N == 0
                    ? RecursionStep<(int, int)>.Return(Result(frame.State.Sum))
                    : RecursionStep<(int, int)>.Next((frame.State.N - 1, frame.State.Sum + frame.State.N));
            },
            new RecursionPolicy { MaxDepth = 128 },
            cycleKey: frame => frame.State.N.ToString());

        var result = await node.RunNodeAsync(new NodeState { Input = "100" });

        result.Signal.Should().Be(NodeSignal.Done);
        result.Response.Data.Should().Be(5050);
    }

    [Fact]
    public async Task NonTailRecursion_UnwindsAsyncContinuations()
    {
        var node = new RecursiveWorkflowNode<int>(
            "factorial",
            state => int.Parse(state.Input),
            (frame, _) => ValueTask.FromResult(
                frame.State <= 1
                    ? RecursionStep<int>.Return(Result(1))
                    : RecursionStep<int>.Next(frame.State - 1, async (parent, child, ct) =>
                    {
                        await Task.Yield();
                        ct.ThrowIfCancellationRequested();
                        return Result(parent.State * (int)child.Response.Data!);
                    })),
            cycleKey: frame => frame.State.ToString());

        var result = await node.RunNodeAsync(new NodeState { Input = "6" });

        result.Response.Data.Should().Be(720);
    }

    [Fact]
    public async Task CycleAndDepthBudgets_ReturnFailedInsteadOfHanging()
    {
        var cycle = new RecursiveWorkflowNode<string>(
            "cycle",
            state => state.Input,
            (frame, _) => ValueTask.FromResult(RecursionStep<string>.Next(frame.State)),
            cycleKey: frame => frame.State);

        var cycleResult = await cycle.RunNodeAsync(new NodeState { Input = "same" });
        cycleResult.Signal.Should().Be(NodeSignal.Failed);
        cycleResult.Response.ErrorMessage.Should().Contain("cycle");

        var depth = new RecursiveWorkflowNode<int>(
            "depth",
            _ => 10,
            (frame, _) => ValueTask.FromResult(RecursionStep<int>.Next(frame.State - 1)),
            new RecursionPolicy { MaxDepth = 2, DetectCycles = false });

        var depthResult = await depth.RunNodeAsync(new NodeState { Input = "go" });
        depthResult.Signal.Should().Be(NodeSignal.Failed);
        depthResult.Response.ErrorMessage.Should().Contain("depth");
    }

    [Fact]
    public async Task DurationBudget_CutsBodyThatDoesNotFinish()
    {
        var node = new RecursiveWorkflowNode<int>(
            "slow",
            _ => 0,
            async (_, ct) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return RecursionStep<int>.Return(Result());
            },
            new RecursionPolicy { MaxDuration = TimeSpan.FromMilliseconds(50) });

        var result = await node.RunNodeAsync(new NodeState { Input = "go" });

        result.Signal.Should().Be(NodeSignal.Failed);
        result.Response.ErrorMessage.Should().Contain("duration");
    }

    [Fact]
    public async Task SameNode_IsReentrantAcrossConcurrentRuns()
    {
        var node = new RecursiveWorkflowNode<int>(
            "countdown",
            state => int.Parse(state.Input),
            async (frame, ct) =>
            {
                await Task.Delay(1, ct);
                return frame.State == 0
                    ? RecursionStep<int>.Return(Result(frame.CallIndex))
                    : RecursionStep<int>.Next(frame.State - 1);
            },
            cycleKey: frame => frame.State.ToString());

        var results = await Task.WhenAll(Enumerable.Range(1, 20)
            .Select(n => node.RunNodeAsync(new NodeState { Input = n.ToString() })));

        results.Should().OnlyContain(r => r.Signal == NodeSignal.Done);
        results.Select(r => (int)r.Response.Data!).Should().Equal(Enumerable.Range(2, 20));
    }

    [Fact]
    public async Task Trace_UsesOneNestedLanePerRecursiveFrame()
    {
        var sink = new InMemoryTraceSink();
        var node = new RecursiveWorkflowNode<int>(
            "rec",
            _ => 2,
            (frame, _) => ValueTask.FromResult(frame.State == 0
                ? RecursionStep<int>.Return(Result())
                : RecursionStep<int>.Next(frame.State - 1)),
            cycleKey: frame => frame.State.ToString());

        using (NodeTrace.Begin(sink))
            await node.RunNodeAsync(new NodeState { Input = "go" });

        sink.Events.Where(e => e.Kind == TraceKind.NodeStart).Select(e => e.Lane)
            .Should().Equal("rec", "rec/rec[1]", "rec/rec[2]");
        sink.Events.Count(e => e.Kind == TraceKind.NodeEnd).Should().Be(3);
    }

    [Fact]
    public async Task MultimodalBatch_ProcessesThreeImagesThenRunsFinalStep()
    {
        var actions = new List<string>();
        var node = new RecursiveWorkflowNode<int>(
            "image-batch",
            state => state.Context!.ImageAttachments.Count,
            (frame, _) =>
            {
                if (frame.State == 0)
                {
                    actions.Add("upload");
                    return ValueTask.FromResult(RecursionStep<int>.Return(Result(
                        artifacts: [new Artifact("remote", "uploaded")])));
                }

                var imageIndex = frame.NodeState.Context!.ImageAttachments.Count - frame.State;
                actions.Add($"image-{imageIndex}");
                return ValueTask.FromResult(RecursionStep<int>.Next(
                    frame.State - 1,
                    (parent, child, _) => ValueTask.FromResult(child with
                    {
                        Artifacts =
                        [
                            new Artifact("image", $"processed-{parent.NodeState.Context!.ImageAttachments.Count - parent.State}"),
                            .. child.Artifacts,
                        ],
                    })));
            },
            cycleKey: frame => frame.State.ToString());

        var context = AgentContext.For("chat", "message", "process and upload") with
        {
            Attachments =
            [
                Image(1),
                Image(2),
                Image(3),
            ],
        };
        var result = await node.RunNodeAsync(new NodeState { Input = context.UserMessage, Context = context });

        actions.Should().Equal("image-0", "image-1", "image-2", "upload");
        result.Artifacts.Select(a => a.Name)
            .Should().Equal("processed-0", "processed-1", "processed-2", "uploaded");
    }

    [Fact]
    public async Task WorkflowNode_PreservesAllAttachmentsForPlainAgentChildren()
    {
        var leaf = new AttachmentCountingAgent();
        var root = new WorkflowNode(
            "root",
            new SequenceStrategy(["leaf"]),
            new Dictionary<string, IAgent> { ["leaf"] = leaf },
            NullLogger<AgentBase<NodeResult>>.Instance);
        var context = AgentContext.For("chat", "message", "look") with
        {
            Attachments = [Image(1), Image(2), Image(3)],
        };

        await root.ProcessAsync(context, CancellationToken.None);

        leaf.SeenImages.Should().Be(3);
    }

    [Fact]
    public async Task ControllableRun_SupportsStopSteerAndQueuedFollowUp()
    {
        var manager = new WorkflowRunManager();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var seenInputs = new ConcurrentQueue<string>();
        var seenSteering = new ConcurrentQueue<string>();

        var node = new RecursiveWorkflowNode<int>(
            "interactive",
            _ => 0,
            async (frame, ct) =>
            {
                if (frame.Depth == 0)
                {
                    seenInputs.Enqueue(frame.NodeState.Input);
                    entered.TrySetResult();
                    await release.Task.WaitAsync(ct);
                    return RecursionStep<int>.Next(1);
                }

                foreach (var message in frame.NodeState.Messages) seenSteering.Enqueue(message.Text);
                return RecursionStep<int>.Return(Result());
            },
            cycleKey: frame => frame.State.ToString());

        var run = manager.Start(node, new NodeState { Input = "first" }, "interactive-run");
        await entered.Task;
        manager.Steer(run.RunId, "change the destination").Should().BeTrue();
        manager.Enqueue(run.RunId, "second").Should().BeTrue();
        manager.Enqueue(run.RunId, "third").Should().BeTrue();
        release.TrySetResult();

        await run.Completion;

        seenSteering.Should().Contain("change the destination");
        seenInputs.Should().Equal("first", "second", "third");
        run.Status.Should().Be(WorkflowRunStatus.Completed);

        var stopEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocking = new RecursiveWorkflowNode<int>(
            "blocking",
            _ => 0,
            async (_, ct) =>
            {
                stopEntered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return RecursionStep<int>.Return(Result());
            });
        var stopped = manager.Start(blocking, new NodeState { Input = "wait" }, "stop-run");
        await stopEntered.Task;
        manager.Stop(stopped.RunId).Should().BeTrue();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stopped.Completion);
        stopped.Status.Should().Be(WorkflowRunStatus.Canceled);
    }

    [Fact]
    public async Task QueuedFollowUp_PreservesPriorContextWithoutMixingItWithCurrentProgress()
    {
        var manager = new WorkflowRunManager();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var seenInputs = new ConcurrentQueue<string>();
        var seenPriorCounts = new ConcurrentQueue<int>();
        var seenLocalCounts = new ConcurrentQueue<int>();
        var seenHandoffs = new ConcurrentQueue<WorkflowHandoff?>();
        var child = new DelegateNode(async (state, ct) =>
        {
            seenInputs.Enqueue(state.Input);
            seenPriorCounts.Enqueue(state.PriorHistory.Count);
            seenLocalCounts.Enqueue(state.History.Count);
            seenHandoffs.Enqueue(state.Handoff);
            if (seenInputs.Count == 1)
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(ct);
            }
            return Result(state.Input);
        });
        var handoffNode = new DelegateNode((state, _) => Task.FromResult(Result(new WorkflowHandoff(
            Summary: $"Inherited {state.PriorHistory.Count} completed root result; steps: " +
                     string.Join(",", state.PriorTranscript.Select(x => x.NodeId)),
            Reason: $"Continue with '{state.Context?.UserMessage ?? state.Input}'."))));
        var root = new WorkflowNode(
            "root",
            new SequenceStrategy(["child"]),
            new Dictionary<string, IAgent> { ["child"] = child },
            NullLogger<AgentBase<NodeResult>>.Instance);

        var run = manager.Start(
            root,
            new NodeState { Input = "first" },
            options: new WorkflowRunOptions { HandoffNode = handoffNode });
        await entered.Task;
        run.Enqueue("second").Should().BeTrue();
        release.TrySetResult();

        await run.Completion;

        seenInputs.Should().Equal("first", "second");
        seenPriorCounts.Should().Equal(0, 1);
        seenLocalCounts.Should().Equal(0, 0);
        seenHandoffs.First().Should().BeNull();
        seenHandoffs.Last()!.Summary.Should().Contain("Inherited 1");
        seenHandoffs.Last()!.Summary.Should().Contain("child");
        seenHandoffs.Last()!.Reason.Should().Contain("second");
        run.Status.Should().Be(WorkflowRunStatus.Completed);
    }

    [Fact]
    public async Task Stop_ReleasesRunEvenWhenChildIgnoresCancellation()
    {
        var child = new NonCooperativeNode();
        var root = new WorkflowNode(
            "root",
            new SequenceStrategy(["child"]),
            new Dictionary<string, IAgent> { ["child"] = child },
            NullLogger<AgentBase<NodeResult>>.Instance);
        var run = new WorkflowRunManager().Start(root, new NodeState { Input = "go" });
        await child.Entered.Task;

        run.Stop().Should().BeTrue();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.Completion);
        run.Status.Should().Be(WorkflowRunStatus.Canceled);

        // Let the deliberately non-cooperative test task finish so the test leaves no background work.
        child.Release();
    }

    [Fact]
    public async Task Run_ReportsPendingHumanInputAndCanBeStoppedWhileWaiting()
    {
        var driver = new WaitingDriver();
        var ask = new AskNode();
        var root = new WorkflowNode(
            "root",
            new SequenceStrategy(["ask"]),
            new Dictionary<string, IAgent> { ["ask"] = ask },
            NullLogger<AgentBase<NodeResult>>.Instance,
            driver: driver);
        var run = new WorkflowRunManager().Start(root, new NodeState { Input = "go" });
        await driver.Asked.Task;

        run.IsWaitingForInput.Should().BeTrue();
        run.PendingInput.Should().Be("Which album?");
        run.Stop().Should().BeTrue();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.Completion);
        run.IsWaitingForInput.Should().BeFalse();
    }

    static MediaAttachment Image(byte value) => new(AttachmentKind.Image, [value], "image/png");

    static NodeResult Result(object? data = null, IReadOnlyList<Artifact>? artifacts = null) => new()
    {
        Response = new AgentResponse
        {
            AgentId = "test",
            AgentName = "Test",
            Role = AgentRole.Custom,
            Data = data,
        },
        Signal = NodeSignal.Done,
        Artifacts = artifacts ?? [],
    };

    sealed class AttachmentCountingAgent() : AgentBase<int>(NullLogger<AgentBase<int>>.Instance)
    {
        public int SeenImages { get; private set; }
        public override string AgentId => "attachment-counter";
        public override string AgentName => "Attachment counter";
        public override AgentRole Role => AgentRole.Custom;

        protected override Task<int> ExecuteCoreAsync(AgentContext ctx, CancellationToken ct)
        {
            SeenImages = ctx.ImageAttachments.Count;
            return Task.FromResult(SeenImages);
        }
    }

    sealed class DelegateNode(Func<NodeState, CancellationToken, Task<NodeResult>> run)
        : AgentBase<NodeResult>(NullLogger<AgentBase<NodeResult>>.Instance), INodeAgent
    {
        public override string AgentId => "delegate";
        public override string AgentName => "Delegate";
        public override AgentRole Role => AgentRole.Custom;

        public Task<NodeResult> RunNodeAsync(NodeState state, CancellationToken ct = default) => run(state, ct);

        protected override Task<NodeResult?> ExecuteCoreAsync(AgentContext ctx, CancellationToken ct)
            => RunNodeAsync(new NodeState { Input = ctx.UserMessage, Context = ctx }, ct)!;
    }

    sealed class NonCooperativeNode() : AgentBase<NodeResult>(NullLogger<AgentBase<NodeResult>>.Instance), INodeAgent
    {
        readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override string AgentId => "non-cooperative";
        public override string AgentName => "Non cooperative";
        public override AgentRole Role => AgentRole.Custom;

        public async Task<NodeResult> RunNodeAsync(NodeState state, CancellationToken ct = default)
        {
            Entered.TrySetResult();
            await _release.Task; // deliberately ignores ct
            return Result();
        }

        public void Release() => _release.TrySetResult();

        protected override Task<NodeResult?> ExecuteCoreAsync(AgentContext ctx, CancellationToken ct)
            => Task.FromResult<NodeResult?>(Result());
    }

    sealed class AskNode() : AgentBase<NodeResult>(NullLogger<AgentBase<NodeResult>>.Instance), INodeAgent
    {
        public override string AgentId => "ask";
        public override string AgentName => "Ask";
        public override AgentRole Role => AgentRole.Custom;

        public Task<NodeResult> RunNodeAsync(NodeState state, CancellationToken ct = default)
            => Task.FromResult(new NodeResult
            {
                Response = new AgentResponse { AgentId = AgentId, AgentName = AgentName, Role = Role },
                Signal = NodeSignal.NeedsInput,
                Ask = "Which album?",
            });

        protected override Task<NodeResult?> ExecuteCoreAsync(AgentContext ctx, CancellationToken ct)
            => Task.FromResult<NodeResult?>(null);
    }

    sealed class WaitingDriver : IDriver
    {
        readonly TaskCompletionSource<string> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Asked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<string> AnswerAsync(string ask, NodeState state, CancellationToken ct = default)
        {
            Asked.TrySetResult();
            return await _answer.Task.WaitAsync(ct);
        }
    }
}
