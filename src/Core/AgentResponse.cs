namespace MiyuAgents.Core;
public sealed record AgentResponse
{
    public required string      AgentId   { get; init; }
    public required string      AgentName { get; init; }
    public required AgentRole   Role      { get; init; }
    public          object?     Data      { get; init; }
    public          bool        IsEmpty   => Data is null;
    public          AgentStatus Status    { get; init; } = AgentStatus.Ok;
    public          string?     ErrorMessage { get; init; }
    public          TimeSpan    Latency   { get; init; }

    public static AgentResponse From(IAgent agent, object? data, TimeSpan latency) =>
        new() { AgentId = agent.AgentId, AgentName = agent.AgentName, Role = agent.Role,
                Data = data, Latency = latency };

    public static AgentResponse Error(IAgent agent, Exception ex, TimeSpan latency) =>
        new() { AgentId = agent.AgentId, AgentName = agent.AgentName, Role = agent.Role,
                Status = AgentStatus.Error, ErrorMessage = ex.Message, Latency = latency };

    public TData? As<TData>() where TData : class => Data as TData;
}