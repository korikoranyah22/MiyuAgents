using Microsoft.Extensions.Logging.Abstractions;
using MiyuAgents.Llm;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Example.Gateways;

/// <summary>
/// OpenAI-compatible gateway. Works for OpenAI and DeepSeek (same API, different base URL).
/// Created per-request — not a singleton.
/// </summary>
public sealed class OpenAiGateway : GatewayBase
{
    private readonly HttpClient _http;
    private readonly string     _baseUrl;
    private readonly string     _model;

    public OpenAiGateway(string apiKey, string model, string baseUrl = "https://api.openai.com")
        : base(NullLogger<GatewayBase>.Instance)
    {
        _model   = model;
        _baseUrl = baseUrl.TrimEnd('/');
        _http    = new HttpClient();
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        _http.Timeout = TimeSpan.FromSeconds(60);
    }

    protected override HttpClient Http => _http;

    public override string                ProviderName    => _baseUrl.Contains("deepseek") ? "deepseek" : "openai";
    public override IReadOnlyList<string> SupportedModels => [_model];

    protected override async Task<LlmResponse> CompleteInternalAsync(LlmRequest req, CancellationToken ct)
    {
        var msgs = new object[] { new { role = "system", content = req.SystemPrompt ?? "" } }
            .Concat(req.Messages.Select(m => (object)new { role = m.Role, content = m.Content }))
            .ToArray();

        var payload = JsonSerializer.Serialize(new
        {
            model       = req.Model,
            messages    = msgs,
            max_tokens  = req.MaxTokens ?? 250,
            temperature = req.Temperature ?? 0.8f
        });

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var res     = await _http.PostAsync($"{_baseUrl}/v1/chat/completions", content, ct);
        res.EnsureSuccessStatusCode();

        using var doc  = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var root       = doc.RootElement;
        var text       = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        var finish     = root.GetProperty("choices")[0].GetProperty("finish_reason").GetString();
        var usage      = root.GetProperty("usage");
        var inTok      = usage.GetProperty("prompt_tokens").GetInt32();
        var outTok     = usage.GetProperty("completion_tokens").GetInt32();

        return new LlmResponse(text, new LlmUsage(inTok, outTok), finish);
    }

    protected override async IAsyncEnumerable<LlmChunk> StreamInternalAsync(
        LlmRequest req, [EnumeratorCancellation] CancellationToken ct)
    {
        var msgs = new object[] { new { role = "system", content = req.SystemPrompt ?? "" } }
            .Concat(req.Messages.Select(m => (object)new { role = m.Role, content = m.Content }))
            .ToArray();

        var payload = JsonSerializer.Serialize(new
        {
            model       = req.Model,
            messages    = msgs,
            max_tokens  = req.MaxTokens ?? 250,
            temperature = req.Temperature ?? 0.8f,
            stream      = true
        });

        using var httpReq = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/chat/completions")
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
            if (data == "[DONE]") break;

            using var doc = JsonDocument.Parse(data);
            var root   = doc.RootElement;
            var choice = root.GetProperty("choices")[0];
            var delta  = choice.GetProperty("delta");

            if (delta.TryGetProperty("content", out var cp) && cp.ValueKind == JsonValueKind.String)
            {
                var token = cp.GetString();
                if (!string.IsNullOrEmpty(token))
                    yield return new LlmChunk(token, false, false, null);
            }

            if (root.TryGetProperty("usage", out var up))
            {
                inputTokens  = up.GetProperty("prompt_tokens").GetInt32();
                outputTokens = up.GetProperty("completion_tokens").GetInt32();
            }
        }

        yield return new LlmChunk("", true, false, new LlmUsage(inputTokens, outputTokens));
    }

}
