using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChildNotes.Core.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChildNotes.Infrastructure.External;

/// <summary>
/// DeepSeek OpenAI 兼容 API 客户端。
/// 请求体遵循 OpenAI Chat Completions 格式（messages + temperature + response_format）。
/// 内置 LLM 调用埋点：记录请求摘要、响应摘要、状态、耗时、错误信息到 ILogger。
/// </summary>
public class DeepSeekClient
{
    private readonly HttpClient _http;
    private readonly DeepSeekOptions _opt;
    private readonly ILogger<DeepSeekClient> _logger;

    public DeepSeekClient(HttpClient http, IOptions<DeepSeekOptions> opt, ILogger<DeepSeekClient> logger)
    {
        _http = http;
        _opt = opt.Value;
        _logger = logger;
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

        // 请求摘要：模型 + 用户输入前 60 字（脱敏长输入）
        var reqSummary = $"model={_opt.Model}, input=\"{TruncateForLog(userMessage, 60)}\"";
        var sw = Stopwatch.StartNew();
        HttpResponseMessage? resp = null;
        string? errBody = null;
        try
        {
            resp = await _http.PostAsJsonAsync("/chat/completions", body, ct);
            if (!resp.IsSuccessStatusCode)
            {
                errBody = await resp.Content.ReadAsStringAsync(ct);
                sw.Stop();
                _logger.LogError("DeepSeek 调用失败 {Ms}ms status={Status} req={Req} err={Err}",
                    sw.ElapsedMilliseconds, (int)resp.StatusCode, reqSummary, TruncateForLog(errBody, 200));
                throw new InvalidOperationException($"DeepSeek API failed ({resp.StatusCode}): {errBody}");
            }

            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;
            var text = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()
                ?? throw new InvalidOperationException("DeepSeek response content is empty");
            var model = root.TryGetProperty("model", out var m) ? m.GetString() ?? _opt.Model : _opt.Model;
            sw.Stop();
            _logger.LogInformation("DeepSeek 调用成功 {Ms}ms req={Req} respLen={Len} respPreview={Preview}",
                sw.ElapsedMilliseconds, reqSummary, text.Length, TruncateForLog(text, 200));
            return (text.Trim(), model);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogWarning("DeepSeek 调用取消 {Ms}ms req={Req}", sw.ElapsedMilliseconds, reqSummary);
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
            _logger.LogError(ex, "DeepSeek 调用异常 {Ms}ms req={Req}", sw.ElapsedMilliseconds, reqSummary);
            throw;
        }
        finally
        {
            resp?.Dispose();
        }
    }

    private static string TruncateForLog(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "(空)";
        return s.Length > max ? s[..max] + "…" : s;
    }
}
