using MiyuAgents.Core;
using MiyuAgents.Llm;
using MiyuAgents.Orchestration.Strategies;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace MiyuAgents.GroupConversations;

public sealed class DefaultGroupConversationOrchestrator : IGroupConversationOrchestrator
{
    private readonly ITurnPolicy          _turnPolicy;
    private readonly IParticipantRouter   _router;
    private readonly int                  _maxRoundsPerTurn;
    private readonly ILogger<DefaultGroupConversationOrchestrator> _logger;

    private readonly List<IParticipant>                _participants = [];
    private readonly List<GroupConversationMessage>    _history      = [];
    private string _conversationId = Guid.NewGuid().ToString();

    public IReadOnlyList<IParticipant>                 Participants => _participants;
    public IReadOnlyList<GroupConversationMessage>     History      => _history;

    public event AsyncEventHandler<GroupMessageProducedEventArgs>? OnMessageProduced;
    public event AsyncEventHandler<ParticipantJoinedEventArgs>?    OnParticipantJoined;
    public event AsyncEventHandler<ParticipantLeftEventArgs>?      OnParticipantLeft;

    public DefaultGroupConversationOrchestrator(
        ITurnPolicy turnPolicy,
        IParticipantRouter router,
        ILogger<DefaultGroupConversationOrchestrator> logger,
        int maxRoundsPerTurn = 3)
    {
        _turnPolicy       = turnPolicy;
        _router           = router;
        _logger           = logger;
        _maxRoundsPerTurn = maxRoundsPerTurn;
    }

    public async Task AddParticipantAsync(IParticipant participant, CancellationToken ct)
    {
        _participants.Add(participant);
        _logger.LogDebug("Participant joined: {Name} ({Kind})",
            participant.DisplayName, participant.Kind);
        await FireAsync(OnParticipantJoined, new ParticipantJoinedEventArgs(
            _conversationId, participant, DateTime.UtcNow));
    }

    public async Task RemoveParticipantAsync(string participantId, CancellationToken ct)
    {
        var p = _participants.FirstOrDefault(x => x.ParticipantId == participantId);
        if (p is null) return;
        _participants.Remove(p);
        _logger.LogDebug("Participant left: {Name}", p.DisplayName);
        await FireAsync(OnParticipantLeft, new ParticipantLeftEventArgs(
            _conversationId, p, DateTime.UtcNow));
    }

    public async Task<GroupTurnResult> SendMessageAsync(
        GroupConversationMessage message, CancellationToken ct)
    {
        _history.Add(message);
        var produced  = new List<GroupConversationMessage>();
        var decisions = new List<TurnSelection>();
        var sw        = Stopwatch.StartNew();

        var ctx = BuildGroupContext(message);

        int round = 1;
        while (round <= _maxRoundsPerTurn)
        {
            var selection = await _turnPolicy.SelectRespondersAsync(
                message, _participants, _history, ctx, ct);
            decisions.Add(selection);

            if (selection.Responders.Count == 0)
            {
                _logger.LogDebug("Turn policy returned no responders at round {Round}", round);
                break;
            }

            if (selection.AllowConcurrentResponses)
            {
                var tasks    = selection.Responders.Select(ap => RespondAsync(ap, ctx, ct));
                var responses = await Task.WhenAll(tasks);
                foreach (var r in responses.Where(r => r is not null))
                {
                    produced.Add(r!);
                    _history.Add(r!);
                    await FireAsync(OnMessageProduced,
                        new GroupMessageProducedEventArgs(message.ConversationId, r!));
                }
            }
            else
            {
                foreach (var agentParticipant in selection.Responders)
                {
                    var r = await RespondAsync(agentParticipant, ctx, ct);
                    if (r is null) continue;
                    produced.Add(r);
                    _history.Add(r);
                    // Rebuild context so next agent sees all responses produced so far this turn.
                    // Re-derive AgentContext.History from _history (excluding current user message
                    // which stays in UserMessage to avoid duplication).
                    var refreshedHistory = _history
                        .Where(m => m.MessageId != message.MessageId)
                        .Select(m => new ConversationMessage(m.Role, m.Content))
                        .ToList();
                    var refreshedBase = ctx.ToAgentContext() with { History = refreshedHistory };
                    ctx = GroupConversationContext.From(refreshedBase, _participants, ctx.Sender, _history, ctx.AddressedToId);
                    await FireAsync(OnMessageProduced,
                        new GroupMessageProducedEventArgs(message.ConversationId, r));
                }
            }

            round++;
        }

        _logger.LogDebug("Group turn complete: {Rounds} rounds, {Count} messages produced",
            round - 1, produced.Count);

        return new GroupTurnResult(
            produced,
            decisions.Select(MapDecision).ToList(),
            round - 1,
            sw.Elapsed);
    }

    private async Task<GroupConversationMessage?> RespondAsync(
        AgentParticipant agentP, GroupConversationContext ctx, CancellationToken ct)
    {
        try
        {
            var response = await agentP.Agent.ProcessAsync(ctx.ToAgentContext(), ct);
            if (response.IsEmpty) return null;

            return new GroupConversationMessage
            {
                MessageId      = Guid.NewGuid().ToString(),
                ConversationId = ctx.ConversationId,
                SenderId       = agentP.ParticipantId,
                SenderName     = agentP.DisplayName,
                SenderKind     = ParticipantKind.Agent,
                Content        = response.As<string>() ?? response.Data?.ToString() ?? "",
                Role           = "assistant",
                Timestamp      = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent {Agent} failed to respond", agentP.DisplayName);
            return null;
        }
    }

    private GroupConversationContext BuildGroupContext(GroupConversationMessage message)
    {
        // Find the sender participant (may not be registered if it's an external user)
        var sender = _participants.FirstOrDefault(p => p.ParticipantId == message.SenderId)
            ?? new HumanParticipant(message.SenderId, message.SenderName);

        // Convert all prior group messages to ConversationMessage so agents can use history.
        // _history already contains the current message (added before this call), so we exclude it.
        // Assistant messages are prefixed with "[SenderName]: " so each agent can distinguish
        // whose words are whose and doesn't mistake another participant's message for its own.
        var priorHistory = _history
            .Take(_history.Count - 1)
            .Select(m => m.Role == "assistant"
                ? new ConversationMessage("assistant", $"[{m.SenderName}]: {m.Content}")
                : new ConversationMessage(m.Role, m.Content))
            .ToList();

        var baseCtx = AgentContext.For(message.ConversationId, message.MessageId, message.Content) with
        {
            History     = priorHistory,
            IsFirstTurn = priorHistory.Count == 0
        };

        return GroupConversationContext.From(
            baseCtx:       baseCtx,
            participants:  _participants,
            sender:        sender,
            groupHistory:  _history,
            addressedToId: message.AddressedToId);
    }

    private static OrchestratorDecision MapDecision(TurnSelection s) =>
        s.Responders.Count == 0
            ? OrchestratorDecision.Empty(0, s.Reason)
            : new OrchestratorDecision(
                s.Responders.Select(r => r.ParticipantId).ToList(),
                s.Reason,
                0);

    private static async Task FireAsync<T>(AsyncEventHandler<T>? handler, T args)
    {
        if (handler is null) return;
        foreach (var del in handler.GetInvocationList().Cast<AsyncEventHandler<T>>())
            await del(null!, args);
    }
}
