using MiyuAgents.Core;

namespace MiyuAgents.Workflows;

/// <summary>
/// El estado que el control-loop de un Node compuesto le pasa a su <see cref="IControlStrategy"/>
/// para decidir el próximo paso. Acumulativo a lo largo del loop (record inmutable → cada paso
/// produce una copia con <c>with</c>).
/// <para>Reservas de futuro del spike (NO en W1): serializable para checkpoint / event-sourcing
/// (§10 #2/#6); un Inbox unificado para steering + eventos externos + cancel (§10 #1).</para>
/// </summary>
public sealed record NodeState
{
    /// <summary>El trigger/entrada inicial del Node (del padre o del Driver).</summary>
    public required string Input { get; init; }

    /// <summary>
    /// Original turn context when the workflow entered through <c>IAgent.ProcessAsync</c>. Keeping
    /// it here preserves identity, history and every media attachment across nested/recursive nodes.
    /// Direct <see cref="INodeAgent.RunNodeAsync"/> callers may leave it null.
    /// </summary>
    public AgentContext? Context { get; init; }

    /// <summary>
    /// Media supplied by direct <see cref="INodeAgent.RunNodeAsync"/> callers that do not have an
    /// <see cref="AgentContext"/>. Context-backed callers normally leave this empty.
    /// </summary>
    public IReadOnlyList<MediaAttachment> Attachments { get; init; } = [];

    /// <summary>
    /// User messages injected while the run is active. <see cref="WorkflowRunHandle.Steer"/> appends
    /// them at safe checkpoints; recursive/custom nodes can inspect them without mutating the root input.
    /// </summary>
    public IReadOnlyList<WorkflowMessage> Messages { get; init; } = [];

    /// <summary>
    /// Results inherited from completed workflow passes. They are semantic context for the next
    /// request, not progress in the current control loop. Keeping them separate prevents strategies
    /// from confusing an earlier pass with children already executed in this pass.
    /// </summary>
    public IReadOnlyList<NodeResult> PriorHistory { get; init; } = [];

    /// <summary>
    /// Bounded internal transcript inherited from completed passes. Unlike <see cref="PriorHistory"/>,
    /// this includes individual child results and human clarification exchanges.
    /// </summary>
    public IReadOnlyList<WorkflowTranscriptEntry> PriorTranscript { get; init; } = [];

    /// <summary>
    /// Optional semantic handoff prepared between passes. Plain agents receive it through
    /// <see cref="EffectiveInput"/>; rich nodes may inspect the structured value directly.
    /// </summary>
    public WorkflowHandoff? Handoff { get; init; }

    /// <summary>Los resultados de los hijos ya ejecutados, en orden (historial del nodo).</summary>
    public IReadOnlyList<NodeResult> History { get; init; } = [];

    /// <summary>La ronda/iteración actual del control-loop (0-based).</summary>
    public int Round { get; init; }

    /// <summary>Pedidos de turno pendientes (bidding, §3.6). La Strategy los arbitra.</summary>
    public IReadOnlyList<Bid> Bids { get; init; } = [];

    /// <summary>Handoff context, current input and live steering, suitable for a plain leaf agent.</summary>
    public string EffectiveInput => string.Join("\n\n", new[]
        {
            Handoff is null
                ? null
                : $"[Workflow handoff]\nPrevious context: {Handoff.Summary}\nWhy this continues: {Handoff.Reason}",
            Input,
        }
        .Concat(Messages.Select(m => $"[User steering]\n{m.Text}"))
        .Where(x => !string.IsNullOrWhiteSpace(x))!);

    /// <summary>Original turn media plus attachments added through steering.</summary>
    public IReadOnlyList<MediaAttachment> EffectiveAttachments =>
        [.. Context?.Attachments ?? [], .. Attachments, .. Messages.SelectMany(m => m.Media)];
}
