using MiyuAgents.Core;
using MiyuAgents.GroupConversations;
using MiyuAgents.Llm;
using MiyuAgents.Memory;
using MiyuAgents.Pipeline;

namespace MiyuAgents.Tests.Unit.Fakes;

/// <summary>
/// Central place for commonly repeated test object construction.
/// Keeps individual test classes free of boilerplate.
/// </summary>
public static class TestBuilders
{
    // ── AgentContext ─────────────────────────────────────────────────────────

    public static AgentContext Context(
        string conversationId = "conv-1",
        string messageId      = "msg-1",
        string userMessage    = "hello",
        string model          = "default")
        => AgentContext.For(conversationId, messageId, userMessage, model);

    // ── PipelineContext ──────────────────────────────────────────────────────

    public static PipelineContext Pipeline() => new()
    {
        EventBus    = NullAgentEventBus.Instance,
        Broadcaster = NullBroadcaster.Instance
    };

    // ── ConversationMessage ──────────────────────────────────────────────────

    public static ConversationMessage UserMessage(string content)
        => new("user", content);

    public static ConversationMessage AssistantMessage(string content)
        => new("assistant", content);

    // ── GroupConversationMessage ─────────────────────────────────────────────

    public static GroupConversationMessage HumanMessage(
        string content,
        string conversationId = "conv-1",
        string senderId       = "user-1",
        string senderName     = "Alice",
        string? addressedToId = null)
        => GroupConversationMessage.FromUser(conversationId, senderId, senderName, content, addressedToId);

    public static GroupConversationMessage AgentMessage(
        string content,
        string conversationId = "conv-1",
        string senderId       = "agent-1",
        string senderName     = "Bot")
        => new()
        {
            MessageId      = Guid.NewGuid().ToString(),
            ConversationId = conversationId,
            SenderId       = senderId,
            SenderName     = senderName,
            SenderKind     = ParticipantKind.Agent,
            Content        = content,
            Role           = "assistant",
            Timestamp      = DateTime.UtcNow
        };

    // ── ExchangeRecord ───────────────────────────────────────────────────────

    public static ExchangeRecord Exchange(
        string userMessage    = "hello",
        string agentResponse  = "world",
        string conversationId = "conv-1",
        string messageId      = "msg-1")
        => new(
            ConversationId: conversationId,
            MessageId:      messageId,
            UserMessage:    userMessage,
            AgentResponse:  agentResponse,
            ProfileId:      "profile-1",
            CharacterId:    "char-1",
            ProfileName:    "TestProfile",
            FullHistory:    [],
            ExchangedAt:    DateTime.UtcNow
        );
}
