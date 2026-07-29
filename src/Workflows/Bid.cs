namespace MiyuAgents.Workflows;

/// <summary>
/// Un pedido de turno de un Node (bidding, §3.6 del spike). El control-loop lo encola en
/// <see cref="NodeState.Bids"/> cuando un hijo emite <see cref="NodeSignal.RequestTurn"/>
/// (reactivo) o cuando un participante contesta que quiere hablar (proactivo, el poll 1/0).
/// La Strategy lo ARBITRA (no bypassea la policy → un agente ansioso no acapara).
/// </summary>
public sealed record Bid(
    string  NodeId,
    int     Priority = 0,
    string? Reason   = null);
