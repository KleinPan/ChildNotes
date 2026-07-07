using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChildNotes.Core.Config;
using Microsoft.Extensions.Options;

namespace ChildNotes.Infrastructure.External;

/// <summary>
/// DeepSeek OpenAI 兼容 API 客户端。
/// 请求体遵循 OpenAI Chat Completions 格式（messages + temperature + response_format）。
/// </summary>
public class DeepSeekClient
{
    private readonly HttpClient _http;
    private readonly DeepSeekOptions _opt;

    public DeepSeekClient(HttpClient http, IOptions<DeepSeekOptions> opt)
    {
        _http = http;
        _opt = opt.Value;
        _http.BaseAddress = new Uri(_opt.BaseUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(120);
        if (!string.IsNullOrEmpty(_opt.ApiKey))
        {
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _opt.ApiKey);
        }
    }

    public virtual async Task<(string text, string model)> ChatAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_opt.ApiKey))
            throw new InvalidOperationException("DeepSeek API key is not configured");

        var body = new
        {
            model = _opt.Model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage },
            },
            temperature = _opt.Temperature,
            max_tokens = _opt.MaxTokens,
            stream = false,
            thinking = new { type = _opt.ThinkingEnabled ? "enabled" : "disabled",
                             reasoning_effort = _opt.ReasoningEffort },
        };

        using var resp = await _http.PostAsJsonAsync("/chat/completions", body, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"DeepSeek API failed ({resp.StatusCode}): {err}");
        }

        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = doc.RootElement;
        var text = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()
            ?? throw new InvalidOperationException("DeepSeek response content is empty");
        var model = root.TryGetProperty("model", out var m) ? m.GetString() ?? _opt.Model : _opt.Model;
        return (text.Trim(), model);
    }
}
