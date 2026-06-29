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

        using var resp = await SendCoreAsync(url, config.ApiKey, body);
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

    /// <summary>
    /// 测试连接：发送一个最小请求（max_tokens=16）验证端点、密钥、模型是否可用。
    /// 成功返回提示信息；失败抛出含具体原因的 InvalidOperationException。
    /// </summary>
    public async Task<string> TestConnectionAsync(LlmConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.ApiBaseUrl))
            throw new InvalidOperationException("API 地址不能为空");
        if (string.IsNullOrWhiteSpace(config.ApiKey))
            throw new InvalidOperationException("API Key 不能为空");
        if (string.IsNullOrWhiteSpace(config.ModelName))
            throw new InvalidOperationException("模型名称不能为空");

        var url = config.ApiBaseUrl.TrimEnd('/') + "/v1/chat/completions";
        var body = new
        {
            model = config.ModelName,
            messages = new[]
            {
                new { role = "system", content = "You are a connection test. Reply with: OK" },
                new { role = "user", content = "ping" },
            },
            temperature = 0.0,
            max_tokens = 16,
        };

        using var resp = await SendCoreAsync(url, config.ApiKey, body);
        var json = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
        {
            var detail = json;
            // 尝试提取 OpenAI 风格 error.message
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("error", out var err) && err.TryGetProperty("message", out var msg))
                    detail = msg.GetString() ?? json;
            }
            catch { }
            throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}：{detail}");
        }

        // 解析返回，验证模型确实回了内容
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var content = choices[0].GetProperty("message").GetProperty("content").GetString();
                return $"连接成功，模型响应：{content}";
            }
            if (doc.RootElement.TryGetProperty("model", out var modelEl))
            {
                return $"连接成功，模型：{modelEl.GetString()}";
            }
            return "连接成功";
        }
        catch
        {
            return "连接成功（响应解析跳过）";
        }
    }

    private static async Task<HttpResponseMessage> SendCoreAsync(string url, string apiKey, object body)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return await HttpClient.SendAsync(req);
    }
}
