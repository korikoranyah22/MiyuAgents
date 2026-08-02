namespace MiyuAgents.Workflows;

/// <summary>Semantic events retained inside a completed workflow result.</summary>
public enum WorkflowTranscriptKind
{
    ChildResult,
    Retry,
    DriverQuestion,
    DriverAnswer,
    Truncated,
}

/// <summary>Bounded metadata for an artifact mentioned in the execution transcript.</summary>
public sealed record WorkflowTranscriptArtifact(
    string Kind,
    string? Name = null,
    string? Id = null,
    string? Preview = null);

/// <summary>
/// One compact, serializable event from the internal execution of a workflow. Payloads are reduced
/// to bounded text previews so transcripts can cross pass boundaries without retaining large files,
/// images or arbitrary object graphs.
/// </summary>
public sealed record WorkflowTranscriptEntry(
    string NodeId,
    string Lane,
    WorkflowTranscriptKind Kind,
    NodeSignal Signal,
    int Round,
    string? Text = null,
    IReadOnlyList<WorkflowTranscriptArtifact>? ProducedArtifacts = null,
    DateTimeOffset At = default)
{
    public IReadOnlyList<WorkflowTranscriptArtifact> Artifacts => ProducedArtifacts ?? [];
    public DateTimeOffset Timestamp => At == default ? DateTimeOffset.UtcNow : At;
}

/// <summary>Small rolling buffer used per execution; oldest entries are discarded first.</summary>
internal sealed class WorkflowTranscriptBuffer(int capacity, int maxTextLength)
{
    readonly List<WorkflowTranscriptEntry> _entries = [];
    readonly int _capacity = Math.Max(1, capacity);
    readonly int _maxTextLength = Math.Max(32, maxTextLength);
    int _dropped;

    public void AddResult(
        string nodeId,
        string lane,
        WorkflowTranscriptKind kind,
        NodeResult result,
        int round)
    {
        AddRange(result.Transcript);
        Add(new WorkflowTranscriptEntry(
            nodeId,
            lane,
            kind,
            result.Signal,
            round,
            ResultText(result),
            result.Artifacts.Select(ArtifactSummary).ToArray(),
            DateTimeOffset.UtcNow));
    }

    public void AddDriverQuestion(string lane, string ask, int round) => Add(new WorkflowTranscriptEntry(
        "driver", lane, WorkflowTranscriptKind.DriverQuestion, NodeSignal.NeedsInput, round,
        Clamp(ask), At: DateTimeOffset.UtcNow));

    public void AddDriverAnswer(string lane, string answer, int round) => Add(new WorkflowTranscriptEntry(
        "driver", lane, WorkflowTranscriptKind.DriverAnswer, NodeSignal.Done, round,
        Clamp(answer), At: DateTimeOffset.UtcNow));

    public IReadOnlyList<WorkflowTranscriptEntry> Snapshot()
    {
        if (_dropped == 0) return _entries.ToArray();
        var marker = new WorkflowTranscriptEntry(
            "workflow", "workflow", WorkflowTranscriptKind.Truncated, NodeSignal.Continue, 0,
            $"{_dropped + 1} older transcript entries were omitted.", At: DateTimeOffset.UtcNow);
        return _capacity == 1
            ? [marker]
            : [marker, .. _entries.TakeLast(_capacity - 1)];
    }

    void AddRange(IEnumerable<WorkflowTranscriptEntry> entries)
    {
        foreach (var entry in entries) Add(entry with { Text = Clamp(entry.Text) });
    }

    void Add(WorkflowTranscriptEntry entry)
    {
        _entries.Add(entry);
        if (_entries.Count <= _capacity) return;
        _entries.RemoveAt(0);
        _dropped++;
    }

    string? ResultText(NodeResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.Response.ErrorMessage))
            return Clamp(result.Response.ErrorMessage);
        if (!string.IsNullOrWhiteSpace(result.Ask)) return Clamp(result.Ask);
        return result.Response.Data switch
        {
            null => null,
            string text => Clamp(text),
            bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal
                => result.Response.Data.ToString(),
            _ => $"<{result.Response.Data.GetType().Name}>",
        };
    }

    WorkflowTranscriptArtifact ArtifactSummary(Artifact artifact) => new(
        artifact.Kind,
        artifact.Name,
        artifact.Id,
        artifact.Payload switch
        {
            null => null,
            string text => Clamp(text),
            bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal
                => artifact.Payload.ToString(),
            _ => $"<{artifact.Payload.GetType().Name}>",
        });

    string? Clamp(string? text)
    {
        if (text is null || text.Length <= _maxTextLength) return text;
        return string.Concat(text.AsSpan(0, _maxTextLength - 1), "…");
    }
}
