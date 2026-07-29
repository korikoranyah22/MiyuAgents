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

    /// <summary>Los resultados de los hijos ya ejecutados, en orden (historial del nodo).</summary>
    public IReadOnlyList<NodeResult> History { get; init; } = [];

    /// <summary>La ronda/iteración actual del control-loop (0-based).</summary>
    public int Round { get; init; }

    /// <summary>Pedidos de turno pendientes (bidding, §3.6). La Strategy los arbitra.</summary>
    public IReadOnlyList<Bid> Bids { get; init; } = [];
}
