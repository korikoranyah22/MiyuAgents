using MiyuAgents.Core;

namespace MiyuAgents.GroupConversations;

/// <summary>
/// Represents any entity that can participate in a group conversation.
/// May be a human user or an AI agent.
/// </summary>
public interface IParticipant
{
    string          ParticipantId   { get; }
    string          DisplayName     { get; }
    ParticipantKind Kind            { get; }
    string?         RoleDescription { get; }
}

public enum ParticipantKind { Human, Agent }

/// <summary>
/// A human user participant. Sends messages but does not run agents.
/// </summary>
public sealed record HumanParticipant(
    string ParticipantId,
    string DisplayName,
    string? RoleDescription = null) : IParticipant
{
    public ParticipantKind Kind => ParticipantKind.Human;
}

/// <summary>
/// An AI agent participant. Wraps an IAgent to participate in group conversations.
/// </summary>
public sealed record AgentParticipant(
    string ParticipantId,
    string DisplayName,
    IAgent Agent,
    string? RoleDescription = null) : IParticipant
{
    public ParticipantKind Kind => ParticipantKind.Agent;
}
