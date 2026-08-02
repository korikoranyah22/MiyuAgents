using System.Collections.Concurrent;
using MiyuAgents.Core;

namespace MiyuAgents.Workflows;

/// <summary>How a user message should enter an already running workflow.</summary>
public enum WorkflowMessageKind
{
    /// <summary>Inject the message at the next safe checkpoint of the active control loop.</summary>
    Steer,

    /// <summary>Run the workflow again with this message after the current pass completes.</summary>
    FollowUp,
}

/// <summary>
/// A user message sent to a live workflow. Attachments travel with the message so a follow-up or
/// steering instruction can add images/audio without losing the multimodal turn context.
/// </summary>
public sealed record WorkflowMessage(
    string Text,
    WorkflowMessageKind Kind,
    IReadOnlyList<MediaAttachment>? Attachments = null,
    DateTimeOffset At = default)
{
    public IReadOnlyList<MediaAttachment> Media => Attachments ?? [];
    public DateTimeOffset Timestamp => At;
}

public enum WorkflowRunStatus { Running, Stopping, Completed, Failed, Canceled }

/// <summary>A compact semantic bridge from a completed pass to the next queued request.</summary>
public sealed record WorkflowHandoff(string Summary, string Reason);

/// <summary>Optional behavior for a managed workflow run.</summary>
public sealed record WorkflowRunOptions
{
    /// <summary>
    /// Subnode asked to summarize inherited context and explain why the queued request continues.
    /// It receives raw previous-pass results through <see cref="NodeState.PriorHistory"/> and may
    /// return either a <see cref="WorkflowHandoff"/> or a summary string in Response.Data.
    /// </summary>
    public INodeAgent? HandoffNode { get; init; }

    /// <summary>Maximum number of internal transcript entries carried across queued passes.</summary>
    public int MaxPriorTranscriptEntries { get; init; } = 400;
}

/// <summary>
/// A controllable workflow execution. It is safe to keep this handle in a host-side registry and
/// wire <see cref="Stop"/>, <see cref="Steer"/> and <see cref="Enqueue"/> to UI actions.
/// </summary>
public sealed class WorkflowRunHandle : IDisposable
{
    readonly object _gate = new();
    readonly CancellationTokenSource _cts;
    readonly ConcurrentQueue<WorkflowMessage> _steering = new();
    readonly Queue<WorkflowMessage> _followUps = new();
    readonly WorkflowRunOptions _options;
    Task<NodeResult> _completion = null!;
    WorkflowRunStatus _status = WorkflowRunStatus.Running;
    DateTimeOffset _lastActivityAt;
    string? _pendingInput;

    internal WorkflowRunHandle(
        string runId,
        CancellationToken externalCancellation,
        WorkflowRunOptions? options)
    {
        RunId = runId;
        StartedAt = DateTimeOffset.UtcNow;
        _lastActivityAt = StartedAt;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCancellation);
        _options = options ?? new WorkflowRunOptions();
    }

    public string RunId { get; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset? EndedAt { get; private set; }
    public WorkflowRunStatus Status { get { lock (_gate) return _status; } }
    public DateTimeOffset LastActivityAt { get { lock (_gate) return _lastActivityAt; } }
    public int PendingFollowUps { get { lock (_gate) return _followUps.Count; } }
    public int PendingSteering => _steering.Count;
    public string? PendingInput { get { lock (_gate) return _pendingInput; } }
    public bool IsWaitingForInput => PendingInput is not null;
    public Task<NodeResult> Completion => _completion;
    public CancellationToken Cancellation => _cts.Token;

    internal void Start(INodeAgent root, NodeState initialState)
        => _completion = RunCoreAsync(root, initialState);

    /// <summary>Request cooperative cancellation. Returns false when the run was already terminal.</summary>
    public bool Stop()
    {
        lock (_gate)
        {
            if (_status is not WorkflowRunStatus.Running) return false;
            _status = WorkflowRunStatus.Stopping;
        }
        _cts.Cancel();
        return true;
    }

    /// <summary>Inject a message into the active pass. The loop consumes it at its next checkpoint.</summary>
    public bool Steer(string text, IReadOnlyList<MediaAttachment>? attachments = null)
        => Post(new WorkflowMessage(text, WorkflowMessageKind.Steer, attachments));

    /// <summary>Queue a new pass after the current workflow result.</summary>
    public bool Enqueue(string text, IReadOnlyList<MediaAttachment>? attachments = null)
        => Post(new WorkflowMessage(text, WorkflowMessageKind.FollowUp, attachments));

    bool Post(WorkflowMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.Text) && message.Media.Count == 0) return false;
        lock (_gate)
        {
            if (_status is not WorkflowRunStatus.Running) return false;
            if (message.At == default) message = message with { At = DateTimeOffset.UtcNow };
            if (message.Kind == WorkflowMessageKind.Steer) _steering.Enqueue(message);
            else _followUps.Enqueue(message);
            _lastActivityAt = DateTimeOffset.UtcNow;
            return true;
        }
    }

    /// <summary>Refresh the run heartbeat from a custom long-running node or tool callback.</summary>
    public void Heartbeat()
    {
        lock (_gate)
            if (_status is WorkflowRunStatus.Running) _lastActivityAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Mark an explicit human-input wait without making the run look stalled. Dispose the returned
    /// scope when the answer arrives. Stop remains available while the run is waiting.
    /// </summary>
    public IDisposable BeginWaitingForInput(string prompt)
    {
        lock (_gate)
        {
            if (_status is WorkflowRunStatus.Running)
            {
                _pendingInput = prompt;
                _lastActivityAt = DateTimeOffset.UtcNow;
            }
        }
        return new InputWait(this, prompt);
    }

    /// <summary>
    /// Drain steering messages at a safe point and append them to the immutable node state. Custom
    /// long-running nodes can call this directly; WorkflowNode and RecursiveWorkflowNode do it for
    /// every loop iteration/frame.
    /// </summary>
    public NodeState Checkpoint(NodeState state)
    {
        Heartbeat();
        var received = new List<WorkflowMessage>();
        while (_steering.TryDequeue(out var message)) received.Add(message);
        return received.Count == 0
            ? state
            : state with { Messages = [.. state.Messages, .. received] };
    }

    async Task<NodeResult> RunCoreAsync(INodeAgent root, NodeState initialState)
    {
        using (WorkflowRunScope.Enter(this))
        {
            var state = initialState;
            try
            {
                while (true)
                {
                    state = Checkpoint(state);
                    var result = await root.RunNodeAsync(state, _cts.Token);
                    _cts.Token.ThrowIfCancellationRequested();

                    if (result.Signal == NodeSignal.Failed)
                    {
                        Finish(WorkflowRunStatus.Failed);
                        return result;
                    }

                    if (!TryTakeNextPass(out var messages))
                        return result;

                    state = await FollowUpStateAsync(state, result, messages, _cts.Token);
                }
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                Finish(WorkflowRunStatus.Canceled);
                throw;
            }
            catch
            {
                Finish(WorkflowRunStatus.Failed);
                throw;
            }
        }
    }

    bool TryTakeNextPass(out IReadOnlyList<WorkflowMessage> messages)
    {
        lock (_gate)
        {
            var next = new List<WorkflowMessage>(1);

            // A steering message posted after the last checkpoint must not disappear. At the pass
            // boundary it becomes the next pass. Follow-ups remain FIFO and run one by one instead
            // of being collapsed into a single synthetic user turn.
            if (_steering.TryDequeue(out var steer)) next.Add(steer);
            else if (_followUps.Count > 0) next.Add(_followUps.Dequeue());

            messages = next;
            if (next.Count > 0) return true;

            // Terminal transition is atomic with the empty-queue check: a concurrent Enqueue/Steer
            // either lands before this check and extends the run, or observes Completed and rejects.
            _status = WorkflowRunStatus.Completed;
            _pendingInput = null;
            EndedAt ??= DateTimeOffset.UtcNow;
            _lastActivityAt = EndedAt.Value;
            return false;
        }
    }

    async Task<NodeState> FollowUpStateAsync(
        NodeState previous,
        NodeResult result,
        IReadOnlyList<WorkflowMessage> messages,
        CancellationToken ct)
    {
        var text = string.Join("\n", messages.Select(m => m.Text).Where(t => !string.IsNullOrWhiteSpace(t)));
        var media = messages.SelectMany(m => m.Media).ToArray();
        var priorHistory = new List<NodeResult>(previous.PriorHistory.Count + previous.History.Count + 1);
        priorHistory.AddRange(previous.PriorHistory);
        priorHistory.AddRange(previous.History);
        priorHistory.Add(result);
        var priorTranscript = previous.PriorTranscript
            .Concat(previous.History.SelectMany(x => x.Transcript))
            .Concat(result.Transcript)
            .TakeLast(Math.Max(1, _options.MaxPriorTranscriptEntries))
            .ToArray();
        AgentContext? context = null;
        if (previous.Context is { } source)
        {
            context = source with
            {
                MessageId = $"{source.MessageId}:follow-up:{Guid.NewGuid():N}",
                UserMessage = text,
                OriginalFullMessage = null,
                Attachments = media,
                IsFirstTurn = false,
                Results = new AgentContextAccumulator(),
            };
        }

        var handoff = await BuildHandoffAsync(
            text,
            media,
            context,
            priorHistory,
            priorTranscript,
            result,
            ct);

        return new NodeState
        {
            Input = text,
            Context = context,
            Attachments = context is null ? media : [],
            // Current-pass progress starts empty. The intentionally inherited history remains fully
            // available to subnodes through PriorHistory and is distilled into Handoff below.
            History = [],
            PriorHistory = priorHistory,
            PriorTranscript = priorTranscript,
            Handoff = handoff,
        };
    }

    async Task<WorkflowHandoff> BuildHandoffAsync(
        string text,
        IReadOnlyList<MediaAttachment> media,
        AgentContext? context,
        IReadOnlyList<NodeResult> priorHistory,
        IReadOnlyList<WorkflowTranscriptEntry> priorTranscript,
        NodeResult previousResult,
        CancellationToken ct)
    {
        var fallback = new WorkflowHandoff(
            Summary: DefaultSummary(previousResult),
            Reason: string.IsNullOrWhiteSpace(text)
                ? "The user added new attachments after the previous pass."
                : $"The user queued a follow-up request: {text}");
        if (_options.HandoffNode is null) return fallback;

        var prompt = "Summarize the inherited workflow context and explain why work continues " +
                     $"with this new request:\n{text}";
        var handoffState = new NodeState
        {
            Input = prompt,
            Context = context,
            Attachments = context is null ? media : [],
            PriorHistory = priorHistory,
            PriorTranscript = priorTranscript,
        };

        NodeResult summarized;
        using (NodeScope.Enter("workflow-handoff"))
            summarized = await _options.HandoffNode.RunNodeAsync(handoffState, ct).WaitAsync(ct);

        if (summarized.Signal == NodeSignal.Failed) return fallback;
        return summarized.Response.Data switch
        {
            WorkflowHandoff structured => structured,
            string summary when !string.IsNullOrWhiteSpace(summary) => fallback with { Summary = summary },
            _ => fallback,
        };
    }

    static string DefaultSummary(NodeResult result)
    {
        var artifactNames = result.Artifacts
            .Select(a => a.Name ?? a.Kind)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Take(8)
            .ToArray();
        var produced = artifactNames.Length == 0
            ? "no named artifacts"
            : string.Join(", ", artifactNames);
        var internalSteps = result.Transcript.Count(x => x.Kind is
            WorkflowTranscriptKind.ChildResult or WorkflowTranscriptKind.Retry);
        return $"The previous pass ended with {result.Signal} after {internalSteps} internal steps " +
               $"and produced {produced}.";
    }

    void Finish(WorkflowRunStatus status)
    {
        lock (_gate)
        {
            _status = status;
            _pendingInput = null;
            EndedAt ??= DateTimeOffset.UtcNow;
            _lastActivityAt = EndedAt.Value;
        }
    }

    public void Dispose() => _cts.Dispose();

    sealed class InputWait(WorkflowRunHandle owner, string prompt) : IDisposable
    {
        public void Dispose()
        {
            lock (owner._gate)
            {
                if (owner._pendingInput == prompt) owner._pendingInput = null;
                owner._lastActivityAt = DateTimeOffset.UtcNow;
            }
        }
    }
}

/// <summary>Ambient access to the current controllable run for custom nodes.</summary>
public static class WorkflowRunScope
{
    static readonly AsyncLocal<WorkflowRunHandle?> CurrentRun = new();

    public static WorkflowRunHandle? Current => CurrentRun.Value;

    internal static IDisposable Enter(WorkflowRunHandle run)
    {
        var previous = CurrentRun.Value;
        CurrentRun.Value = run;
        return new Pop(previous);
    }

    sealed class Pop(WorkflowRunHandle? previous) : IDisposable
    {
        public void Dispose() => CurrentRun.Value = previous;
    }
}

/// <summary>
/// In-memory registry/launcher for controllable workflow runs. Hosts may keep completed handles for
/// status/history and remove them explicitly when their own retention policy expires.
/// </summary>
public sealed class WorkflowRunManager
{
    readonly ConcurrentDictionary<string, WorkflowRunHandle> _runs = new(StringComparer.Ordinal);

    public WorkflowRunHandle Start(
        INodeAgent root,
        NodeState initialState,
        string? runId = null,
        CancellationToken ct = default,
        WorkflowRunOptions? options = null)
    {
        var id = string.IsNullOrWhiteSpace(runId) ? $"wf-{Guid.NewGuid():N}" : runId;
        var handle = new WorkflowRunHandle(id, ct, options);
        if (!_runs.TryAdd(id, handle))
        {
            handle.Dispose();
            throw new InvalidOperationException($"workflow run already exists: '{id}'");
        }
        handle.Start(root, initialState);
        return handle;
    }

    public WorkflowRunHandle? Get(string runId) => _runs.GetValueOrDefault(runId);
    public bool Stop(string runId) => Get(runId)?.Stop() ?? false;
    public bool Steer(string runId, string text, IReadOnlyList<MediaAttachment>? attachments = null)
        => Get(runId)?.Steer(text, attachments) ?? false;
    public bool Enqueue(string runId, string text, IReadOnlyList<MediaAttachment>? attachments = null)
        => Get(runId)?.Enqueue(text, attachments) ?? false;

    public bool Remove(string runId)
    {
        if (!_runs.TryGetValue(runId, out var run) || run.Status is WorkflowRunStatus.Running or WorkflowRunStatus.Stopping)
            return false;
        if (!_runs.TryRemove(runId, out run)) return false;
        run.Dispose();
        return true;
    }
}
