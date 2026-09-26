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
/// 支持主备双端点降级：主用调用失败（网络异常/非 2xx/超时）时自动切换到 Fallback 配置。
/// </summary>
public class DeepSeekClient
{
    private readonly HttpClient _http;
    private readonly DeepSeekOptions _opt;
    private readonly ILogger<DeepSeekClient> _logger;
    private readonly TimeSpan _endpointTimeout;
    private readonly TimeSpan _totalTimeout;

    public DeepSeekClient(HttpClient http, IOptions<DeepSeekOptions> opt, ILogger<DeepSeekClient> logger)
    {
        _http = http;
        _opt = opt.Value;
        _logger = logger;
        // HttpClient 的 BaseAddress/Authorization 在每次调用前动态设置（支持主备双端点），
        // 不在构造函数写死，避免切换备用端点时残留主用配置。
        _http.Timeout = TimeSpan.FromSeconds(120);
        // 单端点超时：主备共享总预算（_totalTimeout），端点超时视为端点故障以触发降级。
        var seconds = _opt.EndpointTimeoutSeconds > 0 ? _opt.EndpointTimeoutSeconds : 20;
        _endpointTimeout = TimeSpan.FromSeconds(seconds);
        // 总时间预算（#15）：必须小于 App 端 30 秒 HTTP 超时。主备共享同一 deadline，
        // 防止"主用耗满 20s + 备用再耗 20s = 40s"超出 App 等待窗口，导致 App 已超时
        // 报错而服务端备用调用照跑（LLM 费用照付、结果被丢弃）。
        var totalSeconds = _opt.TotalTimeoutSeconds > 0 ? _opt.TotalTimeoutSeconds : 28;
        _totalTimeout = TimeSpan.FromSeconds(totalSeconds);
    }

    public virtual async Task<(string text, string model)> ChatAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_opt.ApiKey))
            throw new InvalidOperationException("DeepSeek API key is not configured");

        // 总预算 CTS（#15）：主备共享 deadline，同时受用户取消（ct）约束。
        // 主用失败降级备用时传入同一 requestCt，备用只能使用剩余预算；
        // 预算耗尽时 catch 的 filter（!requestCt.IsCancellationRequested）不再放行降级，直接抛出。
        using var totalCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        totalCts.CancelAfter(_totalTimeout);
        var requestCt = totalCts.Token;
        var startedAt = DateTime.UtcNow;

        // 主用端点
        try
        {
            return await CallEndpointAsync(_opt.BaseUrl, _opt.ApiKey, _opt.Model, systemPrompt, userMessage, requestCt);
        }
        catch (Exception ex) when (_opt.Fallback is { } fb && !string.IsNullOrEmpty(fb.ApiKey) && !requestCt.IsCancellationRequested)
        {
            // 主用失败且配置了备用端点：降级重试。取消/总预算耗尽不降级（用户主动取消或预算已尽）。
            _logger.LogWarning("[AI-LOG] 主用 LLM 调用失败，降级到备用端点（剩余预算 {Remaining:F1}s）model={Model} err={Err}",
                Math.Max(0, (_totalTimeout - (DateTime.UtcNow - startedAt)).TotalSeconds), fb.Model, TruncateForLog(ex.Message, 200));
            return await CallEndpointAsync(fb.BaseUrl, fb.ApiKey, fb.Model, systemPrompt, userMessage, requestCt);
        }
    }

    /// <summary>调用指定端点（主用/备用共用逻辑）。timeout 为单端点超时，超时视为端点故障以触发降级。</summary>
    private async Task<(string text, string model)> CallEndpointAsync(
        string baseUrl, string apiKey, string model, string systemPrompt, string userMessage, CancellationToken ct)
    {
        // 端点级超时：linked CTS 同时受用户取消（ct）和超时控制。
        // 超时抛出的 OCE 会被下方转换为端点故障，从而触发 Fallback 降级（用户主动取消不降级）。
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_endpointTimeout);
        var requestCt = timeoutCts.Token;

        var body = new
        {
            model = model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage },
            },
            temperature = _opt.Temperature,
            max_tokens = _opt.MaxTokens,
            stream = false,
            // thinking 必须显式声明：1xm 的 v4 系列模型缺省该参数时默认开启思考（慢且耗 token），
            // 因此关闭时也要发送 {"type":"disabled"}，不能省略字段
            thinking = _opt.ThinkingEnabled
                ? (object)new { type = "enabled", reasoning_effort = _opt.ReasoningEffort }
                : new { type = "disabled" },
        };

        // 请求摘要：模型 + 用户输入前 60 字（脱敏长输入）
        var reqSummary = $"model={model}, input=\"{TruncateForLog(userMessage, 60)}\"";
        var sw = Stopwatch.StartNew();
        HttpResponseMessage? resp = null;
        string? errBody = null;
        try
        {
            // 每次请求构造独立的 HttpRequestMessage + 绝对 URI（主备端点切换关键）：
            // HttpClient 的 BaseAddress 在发出首个请求后不可再修改（抛 InvalidOperationException），
            // 动态设置 BaseAddress 会导致主用失败后降级备用端点时直接崩溃，备用从未真正生效。
            // 绝对 URI 需拼接 "chat/completions" 相对段，得到 .../v1/chat/completions
            using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), "chat/completions"))
            {
                Content = JsonContent.Create(body),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            resp = await _http.SendAsync(req, requestCt);
            if (!resp.IsSuccessStatusCode)
            {
                errBody = await resp.Content.ReadAsStringAsync(requestCt);
                sw.Stop();
                _logger.LogError("DeepSeek 调用失败 {Ms}ms status={Status} req={Req} err={Err}",
                    sw.ElapsedMilliseconds, (int)resp.StatusCode, reqSummary, TruncateForLog(errBody, 200));
                throw new InvalidOperationException($"DeepSeek API failed ({resp.StatusCode}): {errBody}");
            }

            // 先读响应为字符串再解析：解析失败时可打印原文便于诊断（如网关返回 HTML 首页）
            var rawBody = await resp.Content.ReadAsStringAsync(requestCt);
            string text;
            string respModel;
            try
            {
                using var doc = JsonDocument.Parse(rawBody);
                var root = doc.RootElement;
                text = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()
                    ?? throw new InvalidOperationException("DeepSeek response content is empty");
                respModel = root.TryGetProperty("model", out var m) ? m.GetString() ?? model : model;
            }
            catch (JsonException jex)
            {
                sw.Stop();
                _logger.LogError("[AI-LOG] DeepSeek 响应非合法 JSON {Ms}ms status={Status} req={Req} parseErr={ParseErr} bodyPreview={Body}",
                    sw.ElapsedMilliseconds, (int)resp.StatusCode, reqSummary, jex.Message, TruncateForLog(rawBody, 300));
                throw new InvalidOperationException($"DeepSeek response is not valid JSON (status={resp.StatusCode}, bodyPrefix={TruncateForLog(rawBody, 100)}): {jex.Message}");
            }
            sw.Stop();
            _logger.LogInformation("[AI-LOG] DeepSeek 调用成功 {Ms}ms req={Req} respLen={Len} respPreview={Preview}",
                sw.ElapsedMilliseconds, reqSummary, text.Length, TruncateForLog(text, 200));
            return (text.Trim(), respModel);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogWarning("DeepSeek 调用取消 {Ms}ms req={Req}", sw.ElapsedMilliseconds, reqSummary);
            throw;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // 端点超时（非用户取消）：转换为端点故障抛出，触发 ChatAsync 中的 Fallback 降级
            sw.Stop();
            _logger.LogWarning("[AI-LOG] DeepSeek 端点超时 {Ms}ms (>{Timeout}s) req={Req} url={Url}",
                sw.ElapsedMilliseconds, _endpointTimeout.TotalSeconds, reqSummary, baseUrl);
            throw new InvalidOperationException($"DeepSeek endpoint timeout after {_endpointTimeout.TotalSeconds}s ({baseUrl})");
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
