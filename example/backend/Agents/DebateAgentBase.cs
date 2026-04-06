using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MiyuAgents.Core;
using MiyuAgents.Llm;
using MiyuAgents.Pipeline;

namespace Example.Agents;

/// <summary>
/// Shared base for the two debate agents (LeftAgent / RightAgent).
///
/// Subclasses declare their own AgentId, AgentName, and SystemPrompt.
/// This class owns:
///   - SKIP-token rebuttal logic (CompleteAsync path)
///   - Token-by-token streaming with prefix buffering (StreamAsync path)
///   - Truncation of fabricated multi-turn continuations
///   - IRealtimeBroadcaster wiring via ctx.Metadata["broadcaster"]
/// </summary>
public abstract class DebateAgentBase(string model, ILlmGateway gateway)
    : AgentBase<string>(NullLogger<AgentBase<string>>.Instance)
{
    public override AgentRole Role => AgentRole.Conversation;

    protected abstract string SystemPrompt { get; }

    private const string SkipToken = "SKIP";

    protected override async Task<string?> ExecuteCoreAsync(AgentContext ctx, CancellationToken ct)
    {
        bool isRebuttal = ctx.Metadata.TryGetValue("debate_round", out var dr) && dr is "rebuttal";

        var messages = ctx.History
            .Select(h => new ConversationMessage(h.Role, h.Content, h.Name))
            .ToList();

        // Append the current user message if it isn't already the last entry
        // (guards against double-append when called from different orchestrators).
        bool currentMsgPresent = messages.Any(m => m.Role == "user" && m.Content == ctx.UserMessage);
        if (!currentMsgPresent)
            messages.Add(new ConversationMessage("user", ctx.UserMessage));

        var effectivePrompt = isRebuttal
            ? SystemPrompt + $"""


              Turno de réplica opcional.
              Si el otro agente dijo algo que vale corregir directamente, hazlo en 1-2 oraciones.
              Si no tienes nada relevante que agregar, responde solo con la palabra "{SkipToken}".
              """
            : SystemPrompt;

        var request = new LlmRequest
        {
            Model        = model,
            SystemPrompt = effectivePrompt,
            Messages     = messages,
            MaxTokens    = isRebuttal ? 120 : 250,
            Temperature  = 0.85f
        };

        var broadcaster = ctx.Metadata.TryGetValue("broadcaster", out var b)
            ? (IRealtimeBroadcaster)b
            : NullBroadcaster.Instance;

        string    text  = "";
        LlmUsage? usage = null;

        if (isRebuttal)
        {
            // Full response needed before broadcasting so we can check for SKIP.
            var response = await gateway.CompleteAsync(request, ct);
            text  = response.Content.Trim();
            usage = response.Usage;

            if (text.StartsWith(SkipToken, StringComparison.OrdinalIgnoreCase))
                return null;   // IsEmpty = true → orchestrator silently skips this agent

            var chunk = JsonSerializer.Serialize(new { sender = AgentName, token = text });
            await broadcaster.SendChunkAsync(ctx.ConversationId, chunk, isComplete: true, ct);
        }
        else
        {
            // Stream token-by-token so the frontend renders words as they arrive.
            // We check the accumulated buffer BEFORE emitting each token: if we see
            // "\n[" the LLM is fabricating the next agent's turn — stop and send a
            // replace event so the bubble never shows the bad content.
            var sb       = new StringBuilder();
            bool stopped = false;

            await foreach (var chunk in gateway.StreamAsync(request, ct))
            {
                if (chunk.IsError) continue;
                if (chunk.FinalUsage is not null) usage = chunk.FinalUsage;
                if (string.IsNullOrEmpty(chunk.Delta)) continue;

                sb.Append(chunk.Delta);

                var cutIdx = sb.ToString().IndexOf("\n[", StringComparison.Ordinal);
                if (cutIdx > 0)
                {
                    // Truncate — send replace so the frontend overwrites the bubble
                    text = sb.ToString()[..cutIdx].TrimEnd();
                    var replaceEvt = JsonSerializer.Serialize(new { sender = AgentName, replace = text });
                    await broadcaster.SendChunkAsync(ctx.ConversationId, replaceEvt, isComplete: false, ct);
                    stopped = true;
                    break;
                }

                var tj = JsonSerializer.Serialize(new { sender = AgentName, token = chunk.Delta });
                await broadcaster.SendChunkAsync(ctx.ConversationId, tj, isComplete: false, ct);
            }

            if (!stopped)
                text = sb.ToString().Trim();
        }

        ctx.Results.LlmResponse = text;
        ctx.Results.TokenUsage  = usage;
        return text;
    }

}
