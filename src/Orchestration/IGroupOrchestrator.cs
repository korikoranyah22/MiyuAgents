using MiyuAgents.Core;
using MiyuAgents.Orchestration.Strategies;

namespace MiyuAgents.Orchestration;

/// <summary>
/// Orchestrates a multi-agent conversation loop for a single user turn.
///
/// The loop:
///   1. User message arrives
///   2. Strategy decides which agents respond (up to maxAgentsPerRound)
///   3. Selected agents produce messages
///   4. Strategy sees new history and decides who responds next
///   5. Repeat until strategy returns empty or maxRounds reached
/// </summary>
public interface IGroupOrchestrator
{
    /// <summary>
    /// Run the full multi-agent turn for a single user message.
    /// Returns all agent messages produced during this turn.
    /// </summary>
    Task<GroupTurnResult> RunTurnAsync(
        GroupMessage        userMessage,
        IReadOnlyList<GroupMessage>  history,
        IReadOnlyList<IAgent>        agents,
        AgentContext        ctx,
        CancellationToken   ct = default);
}

public sealed record GroupTurnResult(
    IReadOnlyList<GroupMessage> ProducedMessages,
    IReadOnlyList<OrchestratorDecision> Decisions,
    int RoundsExecuted,
    TimeSpan TotalLatency
);

public sealed record GroupMessage(
    string   Sender,       // user name or agent name
    string   Role,         // "user" | "assistant"
    string   Content,
    DateTime Timestamp
);