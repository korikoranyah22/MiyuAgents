using MiyuAgents.Core;

namespace MiyuAgents.Workflows;

/// <summary>
/// Lo que un Node devuelve a su padre: la <see cref="AgentResponse"/> existente
/// (Data/Status/Latency) + un <see cref="NodeSignal"/> (cómo reaccionar) + los artefactos
/// producidos. ADITIVO sobre AgentResponse: envuelve, no rompe a sus consumidores.
/// </summary>
public sealed record NodeResult
{
    /// <summary>La respuesta cruda del agente/nodo (reusa el tipo existente de MiyuAgents.Core).</summary>
    public required AgentResponse Response { get; init; }

    /// <summary>Cómo debe reaccionar el padre (por defecto: terminó).</summary>
    public NodeSignal Signal { get; init; } = NodeSignal.Done;

    /// <summary>Entregables producidos por este paso.</summary>
    public IReadOnlyList<Artifact> Artifacts { get; init; } = [];

    /// <summary>Si <see cref="Signal"/> == <see cref="NodeSignal.NeedsInput"/>: la pregunta que el Driver debe responder.</summary>
    public string? Ask { get; init; }

    /// <summary>
    /// Bounded internal execution transcript. Composite nodes bubble child transcripts so a later
    /// handoff can explain how the result was reached without depending on a UI-only event stream.
    /// </summary>
    public IReadOnlyList<WorkflowTranscriptEntry> Transcript { get; init; } = [];

    public static NodeResult From(
        AgentResponse response,
        NodeSignal signal = NodeSignal.Done,
        IReadOnlyList<Artifact>? artifacts = null,
        string? ask = null)
        => new() { Response = response, Signal = signal, Artifacts = artifacts ?? [], Ask = ask };
}
