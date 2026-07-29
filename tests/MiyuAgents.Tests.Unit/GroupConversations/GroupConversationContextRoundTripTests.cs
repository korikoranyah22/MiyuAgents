using FluentAssertions;
using MiyuAgents.Core;
using MiyuAgents.GroupConversations;
using Xunit;

namespace MiyuAgents.Tests.Unit.GroupConversations;

// Regresión de la verificación de framework (2026-06-15): GroupConversationContext
// reenviaba todos los campos de AgentContext MENOS Mode (#5) y ParticipantProfileIds,
// que se perdían silenciosamente en From(...) y ToAgentContext(). Estos tests
// fijan el round-trip completo así la migración puede confiar en ctx.Mode.

public class GroupConversationContext_RoundTrip_PreservesMode
{
    private static AgentContext RichBase() =>
        AgentContext.For("conv-1", "msg-1", "hola") with
        {
            ProfileId             = "p-human",
            CharacterId           = "naira",
            ParticipantProfileIds = new[] { "p-human", "agent-kori" },
            Mode                  = ConversationMode.Dream | ConversationMode.Carnal,
        };

    [Fact]
    public void From_ThenToAgentContext_PreservesMode()
    {
        var baseCtx = RichBase();
        var group   = GroupConversationContext.From(
            baseCtx,
            participants: new IParticipant[] { new HumanParticipant("p-human", "Usuario") },
            sender:       new HumanParticipant("p-human", "Usuario"),
            groupHistory: []);

        group.Mode.Should().Be(ConversationMode.Dream | ConversationMode.Carnal);

        var back = group.ToAgentContext();
        back.Mode.Should().Be(ConversationMode.Dream | ConversationMode.Carnal);
        back.IsDream.Should().BeTrue();
        back.IsCarnal.Should().BeTrue();
        back.IsGroup.Should().BeFalse();
    }

    [Fact]
    public void From_ThenToAgentContext_PreservesParticipantProfileIds()
    {
        var baseCtx = RichBase();
        var group   = GroupConversationContext.From(
            baseCtx,
            participants: new IParticipant[] { new HumanParticipant("p-human", "Usuario") },
            sender:       new HumanParticipant("p-human", "Usuario"),
            groupHistory: []);

        group.ParticipantProfileIds.Should().Equal("p-human", "agent-kori");
        group.ToAgentContext().ParticipantProfileIds.Should().Equal("p-human", "agent-kori");
    }

    [Fact]
    public void Default_Mode_IsNormal_WhenBaseHasNone()
    {
        var baseCtx = AgentContext.For("c", "m", "hi"); // Mode default Normal
        var group   = GroupConversationContext.From(
            baseCtx, participants: [], sender: new HumanParticipant("h", "H"), groupHistory: []);

        group.Mode.Should().Be(ConversationMode.Normal);
        group.ToAgentContext().Mode.Should().Be(ConversationMode.Normal);
    }
}
