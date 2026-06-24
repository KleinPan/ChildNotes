using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ChildNotes.Models;

namespace ChildNotes.Services;

public sealed class LlmClient
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(120) };

    public async Task<string> ChatAsync(LlmConfig config, string systemPrompt, string userContent)
    {
        if (!config.Enabled || string.IsNullOrWhiteSpace(config.ApiKey))
            throw new InvalidOperationException("大模型未配置或未启用，请先在设置中配置 API Key。");

        var url = config.ApiBaseUrl.TrimEnd('/') + "/v1/chat/completions";

        var body = new
        {
            model = config.ModelName,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userContent },
            },
            temperature = config.Temperature,
            max_tokens = config.MaxTokens,
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);

        using var resp = await HttpClient.SendAsync(req);
        var json = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"大模型请求失败 ({(int)resp.StatusCode}): {json}");

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            var first = choices[0];
            if (first.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var content))
            {
                return content.GetString() ?? string.Empty;
            }
        }
        throw new InvalidOperationException("大模型返回内容为空");
    }
}
