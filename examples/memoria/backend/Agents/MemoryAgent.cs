using System.Text;
using System.Text.Json;
using Memoria.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using MiyuAgents.Core;
using MiyuAgents.Llm;
using MiyuAgents.Pipeline;

namespace Memoria.Agents;

/// <summary>
/// Conversational agent that reads facts from ctx.Results.Facts (populated by
/// FactMemoryAgent) and streams its response token-by-token.
///
/// Facts are injected into the system prompt so the LLM can reference
/// information the user previously stored.
/// </summary>
public sealed class MemoryAgent(string model, ILlmGateway gateway)
    : AgentBase<string>(NullLogger<AgentBase<string>>.Instance)
{
    public override string    AgentId   => "agent-memory";
    public override string    AgentName => "Asistente";
    public override AgentRole Role      => AgentRole.Conversation;

    protected override async Task<string?> ExecuteCoreAsync(AgentContext ctx, CancellationToken ct)
    {
        // Facts were written by FactMemoryAgent.PopulateAccumulator earlier in the same turn.
        var facts = ctx.Results.Facts.OfType<FactHit>().ToList();

        var systemPrompt = "Eres un asistente conversacional amigable e inteligente.";
        if (facts.Count > 0)
        {
            systemPrompt +=
                "\n\nRecuerdas los siguientes datos relevantes para esta conversación:\n" +
                string.Join("\n", facts.Select(f => $"- {f.Text}"));
        }

        var messages = ctx.History
            .Select(h => new ConversationMessage(h.Role, h.Content))
            .ToList();
        messages.Add(new ConversationMessage("user", ctx.UserMessage));

        var request = new LlmRequest
        {
            Model        = model,
            SystemPrompt = systemPrompt,
            Messages     = messages,
            MaxTokens    = 500,
            Temperature  = 0.75f
        };

        var broadcaster = ctx.Metadata.TryGetValue("broadcaster", out var b)
            ? (IRealtimeBroadcaster)b
            : NullBroadcaster.Instance;

        var sb    = new StringBuilder();
        var usage = (LlmUsage?)null;

        await foreach (var chunk in gateway.StreamAsync(request, ct))
        {
            if (chunk.IsError) continue;
            if (chunk.FinalUsage is not null) usage = chunk.FinalUsage;
            if (string.IsNullOrEmpty(chunk.Delta)) continue;

            sb.Append(chunk.Delta);
            var tj = JsonSerializer.Serialize(new { token = chunk.Delta });
            await broadcaster.SendChunkAsync(ctx.ConversationId, tj, isComplete: false, ct);
        }

        var text = sb.ToString().Trim();
        ctx.Results.LlmResponse = text;
        ctx.Results.TokenUsage  = usage;
        return text;
    }
}
