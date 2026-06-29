using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChildNotes.Data.Repositories;
using ChildNotes.Infrastructure;

namespace ChildNotes.Services;

/// <summary>
/// HTTP API 客户端基类：统一 HttpClient、Bearer 鉴权、{state,msg,data} 信封解析与 401 处理。
/// 派生类仅需实现具体业务方法，复用 SendAsync/SendWithTokenAsync/ExtractData 等工具方法。
/// </summary>
public abstract class BaseApiClient
{
    private protected static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    protected static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>使用配置中的 ServerUrl/Token 发送请求；401 时自动清空 token 并返回 null。</summary>
    protected async Task<HttpResponseMessage?> SendAsync(
        SyncConfigRepository cfgRepo,
        HttpMethod method, string path, string? body, CancellationToken ct)
    {
        var cfg = cfgRepo.Get();
        if (string.IsNullOrWhiteSpace(cfg.ServerUrl) || string.IsNullOrWhiteSpace(cfg.Token))
        {
            DevLogger.Log(GetType().Name, $"{method} {path}: server/token 未配置");
            return null;
        }
        return await SendCoreAsync(cfgRepo, cfg.ServerUrl!, cfg.Token!, method, path, body, ct);
    }

    /// <summary>使用显式 token 发送（用于登录或暂未持久化 token 的多步流程）。</summary>
    protected static async Task<HttpResponseMessage?> SendWithTokenAsync(
        SyncConfigRepository cfgRepo, string serverUrl, string token,
        HttpMethod method, string path, string? body, CancellationToken ct)
        => await SendCoreAsync(cfgRepo, serverUrl, token, method, path, body, ct);

    private static async Task<HttpResponseMessage?> SendCoreAsync(
        SyncConfigRepository cfgRepo, string serverUrl, string token,
        HttpMethod method, string path, string? body, CancellationToken ct)
    {
        var url = serverUrl.TrimEnd('/') + path;
        using var req = new HttpRequestMessage(method, url);
        if (body is not null)
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        try
        {
            var resp = await Http.SendAsync(req, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                cfgRepo.UpdateToken("");
                DevLogger.Log("ApiClient", $"{method} {path}: 401 Unauthorized, token cleared");
                return null;
            }
            if (!resp.IsSuccessStatusCode)
            {
                var text = await resp.Content.ReadAsStringAsync(ct);
                DevLogger.Log("ApiClient", $"{method} {path} fail: {(int)resp.StatusCode} {text}");
                return null;
            }
            return resp;
        }
        catch (Exception ex)
        {
            DevLogger.Log("ApiClient", ex);
            return null;
        }
    }

    /// <summary>从 {state,msg,data} 信封中提取 data 字段并反序列化。</summary>
    protected static T? ExtractData<T>(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return default;
            return JsonSerializer.Deserialize<T>(data.GetRawText(), JsonOpts);
        }
        catch (Exception ex)
        {
            DevLogger.Log("ApiClient", "Parse fail: " + ex.Message);
            return default;
        }
    }

    /// <summary>读取响应体并提取 data 字段。</summary>
    protected static async Task<T?> ReadDataAsync<T>(HttpResponseMessage resp, CancellationToken ct)
    {
        var json = await resp.Content.ReadAsStringAsync(ct);
        return ExtractData<T>(json);
    }

    protected static string Serialize<T>(T obj) => JsonSerializer.Serialize(obj, JsonOpts);

    // ===== V2：抛 SyncException 的版本，供 ApiSyncService 等支持重试的调用方使用 =====

    /// <summary>
    /// V2 版本：与 <see cref="SendWithTokenAsync"/> 行为一致，但失败时抛出
    /// <see cref="SyncException"/> 而非返回 null，便于 <see cref="SyncPolicy"/> 做重试分类。
    /// 401 仍会清空 token，并抛 <see cref="SyncException"/>（Kind=Auth）。
    /// </summary>
    protected static async Task<HttpResponseMessage> SendWithTokenV2Async(
        SyncConfigRepository cfgRepo, string serverUrl, string token,
        HttpMethod method, string path, string? body, CancellationToken ct)
    {
        var url = serverUrl.TrimEnd('/') + path;
        using var req = new HttpRequestMessage(method, url);
        if (body is not null)
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage resp;
        try
        {
            resp = await Http.SendAsync(req, ct);
        }
        catch (TaskCanceledException ex)
        {
            // TaskCanceledException 既可能是取消也可能是超时（HttpClient.Timeout 触发）
            if (ct.IsCancellationRequested) throw;
            throw new SyncException(SyncErrorKind.Timeout, "请求超时: " + path, null, ex);
        }
        catch (HttpRequestException ex)
        {
            throw SyncException.FromHttpRequestException(ex);
        }

        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            cfgRepo.UpdateToken("");
            DevLogger.Log("ApiClient", $"{method} {path}: 401 Unauthorized, token cleared");
            resp.Dispose();
            throw new SyncException(SyncErrorKind.Auth, "鉴权失败", 401);
        }
        if (!resp.IsSuccessStatusCode)
        {
            var code = (int)resp.StatusCode;
            string text;
            try { text = await resp.Content.ReadAsStringAsync(ct); }
            catch { text = ""; }
            DevLogger.Log("ApiClient", $"{method} {path} fail: {code} {text}");
            resp.Dispose();
            throw SyncException.FromHttpStatus(code, $"{method} {path} 失败: {code}");
        }
        return resp;
    }
}
