using MiyuAgents.GroupConversations;

namespace MiyuAgents.Core;

public sealed class DefaultPrivateOrchestrator : IPrivateOrchestrator
{
    private readonly ITurnPolicy _policy;

    public DefaultPrivateOrchestrator(ITurnPolicy policy)
    {
        _policy = policy;
    }

    public async Task<AgentContext> RunPrivateRoundsAsync(
        AgentContext privateCtx, IReadOnlyList<IAgent> subAgents,
        int maxRounds, CancellationToken ct)
    {
        var privateHistory = new List<GroupConversationMessage>();
        int round = 1;

        var participants = subAgents
            .Select(a => (IParticipant)new AgentParticipant(a.AgentId, a.AgentName, a))
            .ToList();

        while (round <= maxRounds)
        {
            var trigger = new GroupConversationMessage
            {
                MessageId      = Guid.NewGuid().ToString(),
                ConversationId = privateCtx.ConversationId,
                SenderId       = "private-orchestrator",
                SenderName     = "InternalOrchestrator",
                SenderKind     = ParticipantKind.Agent,
                Content        = privateCtx.UserMessage,
                Role           = "user",
                Timestamp      = DateTime.UtcNow
            };

            var sender = participants.FirstOrDefault() ?? new HumanParticipant("system", "System");

            var fakeGroupCtx = GroupConversationContext.From(
                baseCtx:       privateCtx,
                participants:  participants,
                sender:        sender,
                groupHistory:  privateHistory,
                addressedToId: null);

            fakeGroupCtx = fakeGroupCtx with { IsFirstTurn = round == 1 };

            var selection = await _policy.SelectRespondersAsync(
                trigger, participants, privateHistory, fakeGroupCtx, ct);

            if (selection.Responders.Count == 0) break;

            foreach (var agentP in selection.Responders)
            {
                var response = await agentP.Agent.ProcessAsync(fakeGroupCtx.ToAgentContext(), ct);
                if (!response.IsEmpty)
                {
                    privateHistory.Add(new GroupConversationMessage
                    {
                        MessageId      = Guid.NewGuid().ToString(),
                        ConversationId = privateCtx.ConversationId,
                        SenderId       = agentP.ParticipantId,
                        SenderName     = agentP.DisplayName,
                        SenderKind     = ParticipantKind.Agent,
                        Content        = response.As<string>() ?? "",
                        Role           = "assistant",
                        Timestamp      = DateTime.UtcNow
                    });
                }
            }

            round++;
        }

        privateCtx.Metadata["__private_history"] = privateHistory;
        return privateCtx;
    }
}
