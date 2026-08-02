using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MiyuAgents.Core;

namespace MiyuAgents.Workflows;

/// <summary>Hard bounds for one recursive-node run.</summary>
public sealed record RecursionPolicy
{
    public int MaxDepth { get; init; } = 64;
    public int MaxCalls { get; init; } = 256;
    public TimeSpan MaxDuration { get; init; } = TimeSpan.FromMinutes(15);
    public bool DetectCycles { get; init; } = true;

    public static readonly RecursionPolicy Default = new();

    internal void Validate()
    {
        if (MaxDepth < 0) throw new ArgumentOutOfRangeException(nameof(MaxDepth));
        if (MaxCalls < 1) throw new ArgumentOutOfRangeException(nameof(MaxCalls));
        if (MaxDuration <= TimeSpan.Zero && MaxDuration != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(MaxDuration));
    }
}

/// <summary>
/// One logical recursive call. <typeparamref name="TState"/> is strongly typed workflow state;
/// <see cref="NodeState"/> retains the original turn, attachments and live steering messages.
/// </summary>
public sealed record RecursiveFrame<TState>(
    TState State,
    NodeState NodeState,
    int Depth,
    int CallIndex);

/// <summary>A continuation executed while the explicit recursion stack unwinds.</summary>
public delegate ValueTask<NodeResult> RecursionContinuation<TState>(
    RecursiveFrame<TState> frame,
    NodeResult childResult,
    CancellationToken ct);

/// <summary>The recursive body either returns a result or schedules one smaller recursive call.</summary>
public abstract record RecursionStep<TState>
{
    private RecursionStep() { }

    public sealed record Complete(NodeResult Result) : RecursionStep<TState>;

    /// <summary>
    /// Continue with <paramref name="NextState"/>. <paramref name="OnReturn"/> is optional: omit it
    /// for tail recursion, or use it to implement non-tail folds such as factorial/tree reduction.
    /// </summary>
    public sealed record Recurse(
        TState NextState,
        RecursionContinuation<TState>? OnReturn = null) : RecursionStep<TState>;

    public static RecursionStep<TState> Return(NodeResult result) => new Complete(result);
    public static RecursionStep<TState> Next(
        TState state,
        RecursionContinuation<TState>? onReturn = null) => new Recurse(state, onReturn);
}

public delegate ValueTask<RecursionStep<TState>> RecursiveNodeBody<TState>(
    RecursiveFrame<TState> frame,
    CancellationToken ct);

/// <summary>
/// Async-first functional recursion over workflow primitives. The node uses an explicit trampoline
/// and continuation stack, so the CLR stack never grows. It supports tail and non-tail recursion,
/// cancellation, a hard duration/depth/call budget, per-run cycle detection, live steering and the
/// regular ambient node trace. All mutable execution data is local, making one node safely reentrant.
/// </summary>
public sealed class RecursiveWorkflowNode<TState> : AgentBase<NodeResult>, INodeAgent
{
    readonly Func<NodeState, TState> _seed;
    readonly RecursiveNodeBody<TState> _body;
    readonly Func<RecursiveFrame<TState>, string?>? _cycleKey;
    readonly RecursionPolicy _policy;

    public RecursiveWorkflowNode(
        string id,
        Func<NodeState, TState> seed,
        RecursiveNodeBody<TState> body,
        RecursionPolicy? policy = null,
        Func<RecursiveFrame<TState>, string?>? cycleKey = null,
        ILogger<AgentBase<NodeResult>>? logger = null,
        string? name = null)
        : base(logger ?? NullLogger<AgentBase<NodeResult>>.Instance)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("node id is required", nameof(id));
        AgentId = id;
        AgentName = name ?? id;
        _seed = seed ?? throw new ArgumentNullException(nameof(seed));
        _body = body ?? throw new ArgumentNullException(nameof(body));
        _policy = policy ?? RecursionPolicy.Default;
        _policy.Validate();
        _cycleKey = cycleKey;
    }

    public override string AgentId { get; }
    public override string AgentName { get; }
    public override AgentRole Role => AgentRole.Orchestration;

    protected override async Task<NodeResult?> ExecuteCoreAsync(AgentContext ctx, CancellationToken ct)
        => await RunNodeAsync(new NodeState { Input = ctx.UserMessage, Context = ctx }, ct);

    public async Task<NodeResult> RunNodeAsync(NodeState state, CancellationToken ct = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (_policy.MaxDuration != Timeout.InfiniteTimeSpan) deadline.CancelAfter(_policy.MaxDuration);

        var lane = NodeScope.Current ?? AgentId;
        try
        {
            return await RunTrampolineAsync(_seed(state), state, lane, deadline.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            var result = Fail($"recursive node exceeded its duration budget ({_policy.MaxDuration})");
            await Emit(TraceKind.Reason, lane, result.Response.ErrorMessage);
            await Emit(TraceKind.NodeEnd, lane, result.Signal.ToString());
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var result = Fail($"recursive node failed: {ex.Message}");
            await Emit(TraceKind.Reason, lane, result.Response.ErrorMessage);
            await Emit(TraceKind.NodeEnd, lane, result.Signal.ToString());
            return result;
        }
    }

    async Task<NodeResult> RunTrampolineAsync(TState initial, NodeState nodeState, string lane, CancellationToken ct)
    {
        var active = new HashSet<ActiveKey>();
        var stack = new Stack<PendingFrame>();
        var current = initial;
        var calls = 0;

        for (var depth = 0; ; depth++)
        {
            ct.ThrowIfCancellationRequested();
            nodeState = WorkflowRunScope.Current?.Checkpoint(nodeState) ?? nodeState;

            if (depth > _policy.MaxDepth)
                return await FailOpenFrames($"maximum recursion depth exceeded ({_policy.MaxDepth})", lane, stack);
            if (++calls > _policy.MaxCalls)
                return await FailOpenFrames($"maximum recursive calls exceeded ({_policy.MaxCalls})", lane, stack);

            var frame = new RecursiveFrame<TState>(current, nodeState, depth, calls);
            ActiveKey? key = null;
            string? keyText = null;
            if (_policy.DetectCycles)
            {
                if (_cycleKey is null)
                {
                    key = new ActiveKey.State(frame.State);
                    keyText = frame.State?.ToString() ?? "<null>";
                }
                else if (_cycleKey(frame) is { } selected)
                {
                    key = new ActiveKey.Custom(selected);
                    keyText = selected;
                }
            }
            if (key is not null && !active.Add(key))
                return await FailOpenFrames($"recursive cycle detected: '{keyText}' is already active", lane, stack);

            var frameLane = FrameLane(lane, depth);
            await Emit(TraceKind.NodeStart, frameLane, keyText);

            RecursionStep<TState> step;
            using (NodeScope.Enter(frameLane))
                step = await _body(frame, ct).AsTask().WaitAsync(ct);

            if (step is RecursionStep<TState>.Recurse recurse)
            {
                stack.Push(new PendingFrame(frame, frameLane, key, recurse.OnReturn));
                current = recurse.NextState;
                continue;
            }

            var result = ((RecursionStep<TState>.Complete)step).Result;
            await Emit(TraceKind.NodeEnd, frameLane, result.Signal.ToString());
            if (key is not null) active.Remove(key);

            while (stack.TryPop(out var parent))
            {
                ct.ThrowIfCancellationRequested();
                if (parent.OnReturn is not null)
                {
                    using (NodeScope.Enter(parent.Lane))
                        result = await parent.OnReturn(parent.Frame, result, ct).AsTask().WaitAsync(ct);
                }
                await Emit(TraceKind.NodeEnd, parent.Lane, result.Signal.ToString());
                if (parent.CycleKey is not null) active.Remove(parent.CycleKey);
            }
            return result;
        }
    }

    async Task<NodeResult> FailOpenFrames(string reason, string lane, Stack<PendingFrame> stack)
    {
        var result = Fail(reason);
        await Emit(TraceKind.Reason, FrameLane(lane, stack.Count), reason);
        while (stack.TryPop(out var frame))
            await Emit(TraceKind.NodeEnd, frame.Lane, result.Signal.ToString());
        return result;
    }

    string FrameLane(string lane, int depth) => depth == 0 ? lane : $"{lane}/{AgentId}[{depth}]";

    Task Emit(TraceKind kind, string lane, string? text = null)
    {
        var sink = NodeTrace.Current;
        return sink is null
            ? Task.CompletedTask
            : sink.EmitAsync(new TraceEvent(AgentId, lane, kind, Actor: AgentId, Text: text, At: DateTimeOffset.UtcNow));
    }

    NodeResult Fail(string reason) => new()
    {
        Response = new AgentResponse
        {
            AgentId = AgentId,
            AgentName = AgentName,
            Role = Role,
            Status = AgentStatus.Error,
            ErrorMessage = reason,
        },
        Signal = NodeSignal.Failed,
    };

    sealed record PendingFrame(
        RecursiveFrame<TState> Frame,
        string Lane,
        ActiveKey? CycleKey,
        RecursionContinuation<TState>? OnReturn);

    abstract record ActiveKey
    {
        public sealed record State(TState Value) : ActiveKey;
        public sealed record Custom(string Value) : ActiveKey;
    }
}
