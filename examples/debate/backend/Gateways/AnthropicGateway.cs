using Microsoft.Extensions.Logging.Abstractions;
using MiyuAgents.Llm;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Example.Gateways;

/// <summary>
/// Anthropic Claude gateway.
/// Note: Anthropic requires strictly alternating user/assistant messages.
/// Group conversations produce consecutive assistant messages (one per agent);
/// CollapseConsecutive merges them so the API doesn't reject the request.
/// </summary>
public sealed class AnthropicGateway : GatewayBase
{
    private readonly HttpClient _http;
    private readonly string     _model;

    public AnthropicGateway(string apiKey, string model)
        : base(NullLogger<GatewayBase>.Instance)
    {
        _model = model;
        _http  = new HttpClient();
        _http.DefaultRequestHeaders.Add("x-api-key",         apiKey);
        _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        _http.Timeout = TimeSpan.FromSeconds(60);
    }

    protected override HttpClient Http => _http;

    public override string                ProviderName    => "anthropic";
    public override IReadOnlyList<string> SupportedModels => [_model];

    protected override async Task<LlmResponse> CompleteInternalAsync(LlmRequest req, CancellationToken ct)
    {
        // Anthropic requires strict user/assistant alternation.
        // Group history may have consecutive "assistant" messages (left agent, right agent).
        // We merge them into one assistant turn so the API accepts the request.
        var normalizedMessages = CollapseConsecutive(req.Messages)
            .Select(m => new { role = m.Role, content = m.Content })
            .ToArray();

        var payload = JsonSerializer.Serialize(new
        {
            model      = req.Model,
            system     = req.SystemPrompt ?? "",
            messages   = normalizedMessages,
            max_tokens = req.MaxTokens ?? 250
        });

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var res     = await _http.PostAsync("https://api.anthropic.com/v1/messages", content, ct);
        res.EnsureSuccessStatusCode();

        using var doc  = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var root       = doc.RootElement;
        var text       = root.GetProperty("content")[0].GetProperty("text").GetString() ?? "";
        var usage      = root.GetProperty("usage");
        var inTok      = usage.GetProperty("input_tokens").GetInt32();
        var outTok     = usage.GetProperty("output_tokens").GetInt32();

        return new LlmResponse(text, new LlmUsage(inTok, outTok), "end_turn");
    }

    protected override async IAsyncEnumerable<LlmChunk> StreamInternalAsync(
        LlmRequest req, [EnumeratorCancellation] CancellationToken ct)
    {
        var normalizedMessages = CollapseConsecutive(req.Messages)
            .Select(m => new { role = m.Role, content = m.Content })
            .ToArray();

        var payload = JsonSerializer.Serialize(new
        {
            model      = req.Model,
            system     = req.SystemPrompt ?? "",
            messages   = normalizedMessages,
            max_tokens = req.MaxTokens ?? 250,
            stream     = true
        });

        using var httpReq = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        using var res = await _http.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();

        using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        int inputTokens = 0, outputTokens = 0;

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (!line.StartsWith("data: ")) continue;
            var data = line[6..].Trim();
            if (string.IsNullOrEmpty(data)) continue;

            using var doc  = JsonDocument.Parse(data);
            var root       = doc.RootElement;
            var type       = root.GetProperty("type").GetString();

            if (type == "content_block_delta")
            {
                var delta = root.GetProperty("delta");
                if (delta.GetProperty("type").GetString() == "text_delta")
                {
                    var text = delta.GetProperty("text").GetString();
                    if (!string.IsNullOrEmpty(text))
                        yield return new LlmChunk(text, false, false, null);
                }
            }
            else if (type == "message_start")
            {
                var usage   = root.GetProperty("message").GetProperty("usage");
                inputTokens = usage.GetProperty("input_tokens").GetInt32();
            }
            else if (type == "message_delta")
            {
                var usage    = root.GetProperty("usage");
                outputTokens = usage.GetProperty("output_tokens").GetInt32();
            }
        }

        yield return new LlmChunk("", true, false, new LlmUsage(inputTokens, outputTokens));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Merges consecutive messages with the same role into one, which Anthropic requires.
    /// When merging, speaker names are prepended to each segment so the model can still
    /// distinguish who said what — Anthropic has no native "name" field on messages.
    /// </summary>
    private static IEnumerable<ConversationMessage> CollapseConsecutive(
        IEnumerable<ConversationMessage> messages)
    {
        ConversationMessage? acc = null;
        foreach (var m in messages)
        {
            if (acc is null) { acc = m; continue; }

            if (acc.Role == m.Role)
            {
                // Label each segment so the model knows who said what after merging.
                var left  = acc.Name  is { } ln ? $"[{ln}]: {acc.Content}"  : acc.Content;
                var right = m.Name    is { } rn ? $"[{rn}]: {m.Content}"    : m.Content;
                acc = new ConversationMessage(acc.Role, left + "\n\n" + right);
            }
            else
            {
                yield return acc;
                acc = m;
            }
        }
        if (acc is not null) yield return acc;
    }
}
