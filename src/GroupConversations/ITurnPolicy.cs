using MiyuAgents.Core;

namespace MiyuAgents.GroupConversations;

/// <summary>
/// Decides which agent participants should respond to a given message.
/// Different policies implement different turn-taking behaviors.
/// </summary>
public interface ITurnPolicy
{
    string PolicyName { get; }

    Task<TurnSelection> SelectRespondersAsync(
        GroupConversationMessage        message,
        IReadOnlyList<IParticipant>     participants,
        IReadOnlyList<GroupConversationMessage> history,
        GroupConversationContext        ctx,
        CancellationToken               ct);
}

/// <summary>Result of a turn policy decision.</summary>
public sealed record TurnSelection(
    IReadOnlyList<AgentParticipant> Responders,
    string                          Reason,
    bool                            AllowConcurrentResponses = false
)
{
    public static TurnSelection None(string reason) =>
        new([], reason);
}

// ── Built-in policies ────────────────────────────────────────────────────────

/// <summary>
/// All agent participants respond to every message.
/// Good for panels, debates, or when all perspectives are needed.
/// </summary>
public sealed class BroadcastTurnPolicy : ITurnPolicy
{
    public string PolicyName => "broadcast";

    public Task<TurnSelection> SelectRespondersAsync(
        GroupConversationMessage message,
        IReadOnlyList<IParticipant> participants,
        IReadOnlyList<GroupConversationMessage> history,
        GroupConversationContext ctx,
        CancellationToken ct)
    {
        var agents = participants.OfType<AgentParticipant>().ToList();
        return Task.FromResult(new TurnSelection(agents, "broadcast — all agents respond",
            AllowConcurrentResponses: true));
    }
}

/// <summary>
/// Only responds when explicitly @-addressed (AddressedToId matches).
/// Falls back to broadcast if no specific address.
/// </summary>
public sealed class AddressedOnlyPolicy : ITurnPolicy
{
    public string PolicyName => "addressed-only";

    public Task<TurnSelection> SelectRespondersAsync(
        GroupConversationMessage message,
        IReadOnlyList<IParticipant> participants,
        IReadOnlyList<GroupConversationMessage> history,
        GroupConversationContext ctx,
        CancellationToken ct)
    {
        if (message.AddressedToId is not null)
        {
            var target = participants
                .OfType<AgentParticipant>()
                .FirstOrDefault(a => a.ParticipantId == message.AddressedToId);

            return Task.FromResult(target is not null
                ? new TurnSelection([target], $"addressed to {target.DisplayName}")
                : TurnSelection.None($"addressed to unknown participant {message.AddressedToId}"));
        }

        // Not addressed — nobody responds (human message to the group, not to an agent)
        return Task.FromResult(TurnSelection.None("no address — awaiting explicit @mention"));
    }
}

/// <summary>
/// Passes all messages from humans through to agents.
/// Used as baseline in simple chatbots.
/// </summary>
public sealed class HumanOnlyPassthroughPolicy : ITurnPolicy
{
    public string PolicyName => "human-passthrough";

    public Task<TurnSelection> SelectRespondersAsync(
        GroupConversationMessage message,
        IReadOnlyList<IParticipant> participants,
        IReadOnlyList<GroupConversationMessage> history,
        GroupConversationContext ctx,
        CancellationToken ct)
    {
        // Only respond to human messages
        if (message.SenderKind != ParticipantKind.Human)
            return Task.FromResult(TurnSelection.None("agent-to-agent message — no passthrough"));

        var agents = participants.OfType<AgentParticipant>().ToList();
        return Task.FromResult(new TurnSelection(agents, "human message — all agents respond"));
    }
}

/// <summary>
/// Uses a fast LLM call to decide which agent(s) should respond.
/// Most flexible but has LLM latency overhead.
/// </summary>
public sealed class ModeratedTurnPolicy(
    MiyuAgents.Llm.ILlmGateway llm,
    string moderatorModel = "default") : ITurnPolicy
{
    public string PolicyName => "moderated";

    public async Task<TurnSelection> SelectRespondersAsync(
        GroupConversationMessage message,
        IReadOnlyList<IParticipant> participants,
        IReadOnlyList<GroupConversationMessage> history,
        GroupConversationContext ctx,
        CancellationToken ct)
    {
        var agents = participants.OfType<AgentParticipant>().ToList();
        if (agents.Count == 0) return TurnSelection.None("no agent participants");
        if (agents.Count == 1) return new TurnSelection(agents, "single agent — auto-selected");

        var agentList = string.Join("\n", agents.Select(a =>
            $"- {a.ParticipantId}: {a.DisplayName} ({a.RoleDescription ?? "general"})"));

        var prompt = $"""
            Given this message: "{message.Content}"

            Available agents:
            {agentList}

            Which agent(s) should respond? Return only comma-separated agent IDs.
            If multiple, they may respond concurrently. Return "none" if no agent should respond.
            """;

        var response = await llm.CompleteAsync(new MiyuAgents.Llm.LlmRequest
        {
            Model        = moderatorModel,
            SystemPrompt = "You are a conversation moderator. Select which agent(s) should respond.",
            Messages     = [new MiyuAgents.Llm.ConversationMessage("user", prompt)]
        }, ct);

        var content = response.Content.Trim().ToLowerInvariant();
        if (content == "none") return TurnSelection.None("moderator: no response needed");

        var selectedIds = content.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var selected    = agents.Where(a => selectedIds.Contains(a.ParticipantId)).ToList();

        return selected.Count > 0
            ? new TurnSelection(selected, $"moderator selected: {string.Join(", ", selected.Select(a => a.DisplayName))}")
            : new TurnSelection(agents.Take(1).ToList(), "moderator fallback: first agent");
    }
}
