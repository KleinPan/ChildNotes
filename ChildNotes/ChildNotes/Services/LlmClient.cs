using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ChildNotes.Infrastructure;
using ChildNotes.Models;

namespace ChildNotes.Services;

public sealed class LlmClient
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(120) };

    public async Task<string> ChatAsync(LlmConfig config, string systemPrompt, string userContent, CancellationToken ct = default)
    {
        if (!config.Enabled)
            throw new InvalidOperationException("大模型未启用，请先在设置中开启。");

        var url = BuildChatCompletionsUrl(config.ApiBaseUrl);
        var body = BuildRequestBody(config.ModelName, config.Temperature, config.MaxTokens, systemPrompt, userContent);

        // LLM 调用埋点：记录请求参数、响应摘要、状态、耗时、错误信息
        var sw = Stopwatch.StartNew();
        var requestSummary = BuildRequestSummary(config.ModelName, userContent);
        HttpResponseMessage? resp = null;
        string? json = null;
        try
        {
            resp = await SendCoreAsync(url, config.ApiKey, body, ct);
            json = await resp.Content.ReadAsStringAsync(ct);
            sw.Stop();

            if (!resp.IsSuccessStatusCode)
            {
                DevLogger.LogLlmCall(
                    tag: "LlmClient",
                    requestSummary: requestSummary,
                    responseSummary: TruncateForLog(json),
                    success: false,
                    elapsedMs: sw.ElapsedMilliseconds,
                    errorMessage: $"HTTP {(int)resp.StatusCode}");
                throw new InvalidOperationException($"大模型请求失败 ({(int)resp.StatusCode}): {json}");
            }

            var content = ExtractContent(json);
            DevLogger.LogLlmCall(
                tag: "LlmClient",
                requestSummary: requestSummary,
                responseSummary: TruncateForLog(content),
                success: true,
                elapsedMs: sw.ElapsedMilliseconds);
            return content;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            sw.Stop();
            DevLogger.LogLlmCall("LlmClient", requestSummary, null, false, sw.ElapsedMilliseconds, "用户取消");
            throw;
        }
        catch (InvalidOperationException)
        {
            // 已在上面记录日志，直接重新抛出
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            DevLogger.LogLlmCall("LlmClient", requestSummary, json is null ? null : TruncateForLog(json),
                false, sw.ElapsedMilliseconds, ex.Message);
            throw;
        }
        finally
        {
            resp?.Dispose();
        }
    }

    /// <summary>
    /// 构造用于日志的请求摘要：模型名 + 用户输入前若干字（避免日志过长，且脱敏用户输入）。
    /// </summary>
    private static string BuildRequestSummary(string modelName, string userContent)
    {
        var preview = userContent.Length > 60 ? userContent[..60] + "…" : userContent;
        return $"model={modelName}, input=\"{preview}\"";
    }

    /// <summary>截断响应摘要，避免单条日志过长（如完整 JSON 响应可能数千字）。</summary>
    private static string TruncateForLog(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "(空)";
        return s.Length > 200 ? s[..200] + "…" : s;
    }

    /// <summary>
    /// 测试连接：发送一个最小请求（max_tokens=16）验证端点、密钥、模型是否可用。
    /// 成功返回提示信息；失败抛出含具体原因的 InvalidOperationException。
    /// </summary>
    public async Task<string> TestConnectionAsync(LlmConfig config, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(config.ApiBaseUrl))
            throw new InvalidOperationException("API 地址不能为空");
        if (string.IsNullOrWhiteSpace(config.ModelName))
            throw new InvalidOperationException("模型名称不能为空");

        var url = BuildChatCompletionsUrl(config.ApiBaseUrl);
        var body = BuildRequestBody(
            config.ModelName,
            temperature: 0.0,
            maxTokens: 16,
            systemPrompt: "You are a connection test. Reply with: OK",
            userContent: "ping");

        var sw = Stopwatch.StartNew();
        using var resp = await SendCoreAsync(url, config.ApiKey, body, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        sw.Stop();

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
            DevLogger.LogLlmCall("LlmClient/Test", $"model={config.ModelName}, ping", null, false, sw.ElapsedMilliseconds, $"HTTP {(int)resp.StatusCode}");
            throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}：{detail}");
        }

        // 解析返回，验证模型确实回了内容
        try
        {
            var content = ExtractContent(json);
            DevLogger.LogLlmCall("LlmClient/Test", $"model={config.ModelName}, ping", content, true, sw.ElapsedMilliseconds);
            return $"连接成功，模型响应：{content}";
        }
        catch
        {
            // content 为空但 HTTP 200，说明端点可达。回退到 model 字段
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("model", out var modelEl))
                {
                    var modelName = modelEl.GetString();
                    DevLogger.LogLlmCall("LlmClient/Test", $"model={config.ModelName}, ping", $"model={modelName}", true, sw.ElapsedMilliseconds);
                    return $"连接成功，模型：{modelName}";
                }
            }
            catch { }
            return "连接成功（响应解析跳过）";
        }
    }

    private static async Task<HttpResponseMessage> SendCoreAsync(string url, string? apiKey, object body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        // ApiKey 为空时不加 Authorization 头，支持本地大模型（Ollama/vLLM/LM Studio 等）
        if (!string.IsNullOrWhiteSpace(apiKey))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return await HttpClient.SendAsync(req, ct);
    }

    /// <summary>
    /// 构造 chat/completions 请求体。使用 Dictionary 以便附加非标准参数：
    /// - enable_thinking=false：关闭 qwen3 系列思考模式，避免输出 reasoning_content 浪费 token
    ///   （实测 qwen35-397b-a17b-fp8 开思考 29s/1232 token，关思考 3s/53 token）。
    ///   OpenAI/DeepSeek 等不识别此参数会自动忽略，向后兼容。
    /// </summary>
    private static Dictionary<string, object?> BuildRequestBody(
        string modelName, double temperature, int maxTokens, string systemPrompt, string userContent)
    {
        return new Dictionary<string, object?>
        {
            ["model"] = modelName,
            ["messages"] = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userContent },
            },
            ["temperature"] = temperature,
            ["max_tokens"] = maxTokens,
            ["enable_thinking"] = false,
        };
    }

    /// <summary>
    /// 从 chat/completions 响应中提取模型输出文本。
    /// 优先取 message.content；若为空（思考模式下思考阶段 content 可能空串），回退到 reasoning_content。
    /// 若两者都无，抛"返回内容为空"。
    /// </summary>
    private static string ExtractContent(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            if (choices[0].TryGetProperty("message", out var msg))
            {
                var content = msg.TryGetProperty("content", out var cEl) ? cEl.GetString() : null;
                if (!string.IsNullOrWhiteSpace(content))
                    return content!;
                // 兼容 qwen3 思考模式：content 为空时回退到 reasoning_content
                if (msg.TryGetProperty("reasoning_content", out var rEl))
                {
                    var reasoning = rEl.GetString();
                    if (!string.IsNullOrWhiteSpace(reasoning))
                        return reasoning!;
                }
            }
        }
        throw new InvalidOperationException("大模型返回内容为空");
    }

    /// <summary>
    /// 构造 chat/completions 完整 URL。
    /// 智能识别：若 ApiBaseUrl 已以 /chat/completions 结尾，直接使用；否则追加 /v1/chat/completions。
    /// 这样既能兼容 OpenAI/DeepSeek/通义千问（v1），也能支持仅提供 v2 路径的服务商
    /// （如阿里云百炼 qwen3 模型走 /api/ais-v2/chat/completions，用户可直接在 API 地址栏填完整路径）。
    /// </summary>
    private static string BuildChatCompletionsUrl(string apiBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
            throw new InvalidOperationException("API 地址不能为空");
        var base_ = apiBaseUrl.Trim();
        const string suffix = "/chat/completions";
        // 大小写不敏感判断是否已含完整路径
        if (base_.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return base_;
        return base_.TrimEnd('/') + "/v1/chat/completions";
    }
}
