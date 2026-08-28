using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChildNotes.Data.Repositories;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Services.Storage;

namespace ChildNotes.Services;

/// <summary>
/// HTTP API 客户端基类：统一 HttpClient、Bearer 鉴权、{state,msg,data} 信封解析与 401 处理。
/// v5 重构：
///   - 移除 sync_config.username/password 自动登录（/api/auth/login）
///   - AccessToken 从 ISecureStorage 读取（非明文 SQLite）
///   - 401 时触发 RefreshToken Rotation（/api/auth/refresh），仍失败则清空登录态并返回 null
///   - 认证失败不删除业务数据，仅清空 SecureStorage 与 CloudUserId（由 AuthService.LogoutAsync 处理）
/// </summary>
public abstract class BaseApiClient
{
    private protected static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    protected static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = null,
        // 后端 ASP.NET Core 默认 camelCase 序列化（serverTime/expireAt 等），
        // 前端 DTO 用 PascalCase（ServerTime/ExpireAt）。开启大小写不敏感，
        // 避免字段名大小写不匹配导致 DateTime 等类型用默认值（0001-01-01）。
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// 使用 SecureStorage 中的 AccessToken 发送请求。
    /// 401 时自动尝试 RefreshToken Rotation；仍失败则返回 null（登录态已清空，业务数据保留）。
    /// </summary>
    protected async Task<HttpResponseMessage?> SendAsync(
        SyncConfigRepository cfgRepo,
        HttpMethod method, string path, string? body, CancellationToken ct)
    {
        var cfg = cfgRepo.Get();
        if (string.IsNullOrWhiteSpace(cfg.ServerUrl))
        {
            DevLogger.Log(GetType().Name, $"{method} {path}: server 未配置");
            return null;
        }
        var serverUrl = string.IsNullOrWhiteSpace(cfg.ServerUrl)
            ? ServerEndpoints.Primary
            : cfg.ServerUrl!;

        var auth = ServiceProvider.Instance.AuthService;
        var token = await auth.GetAccessTokenAsync(ct);
        if (string.IsNullOrWhiteSpace(token))
        {
            // AccessToken 缺失：尝试用 RefreshToken 续期
            token = await auth.RefreshAccessTokenAsync(ct);
            if (string.IsNullOrEmpty(token))
            {
                DevLogger.Log(GetType().Name, $"{method} {path}: token 缺失且 Refresh 失败");
                return null;
            }
        }

        var resp = await SendCoreAsync(serverUrl, token, method, path, body, ct, swallowNonSuccess: true);
        // 401 时 SendCoreAsync 已删除 AccessToken，这里尝试 Refresh 续期重试一次
        if (resp is null && string.IsNullOrEmpty(await auth.GetAccessTokenAsync(ct)))
        {
            var newToken = await auth.RefreshAccessTokenAsync(ct);
            if (!string.IsNullOrEmpty(newToken))
            {
                resp = await SendCoreAsync(serverUrl, newToken, method, path, body, ct, swallowNonSuccess: true);
            }
        }
        return resp;
    }

    /// <summary>
    /// 与 <see cref="SendAsync"/> 行为一致，但非 2xx 响应会返回 <see cref="HttpResponseMessage"/>
    /// 而非 null，供调用方读取后端业务错误体（{state,msg,data} 信封中的 msg/code）。
    /// 仅以下情况返回 null：server 未配置、token 缺失且 Refresh 失败、网络异常、401 重试仍失败。
    /// </summary>
    protected async Task<HttpResponseMessage?> SendWithErrorAsync(
        SyncConfigRepository cfgRepo,
        HttpMethod method, string path, string? body, CancellationToken ct)
    {
        var cfg = cfgRepo.Get();
        if (string.IsNullOrWhiteSpace(cfg.ServerUrl))
        {
            DevLogger.Log(GetType().Name, $"{method} {path}: server 未配置");
            return null;
        }
        var serverUrl = string.IsNullOrWhiteSpace(cfg.ServerUrl)
            ? ServerEndpoints.Primary
            : cfg.ServerUrl!;

        var auth = ServiceProvider.Instance.AuthService;
        var token = await auth.GetAccessTokenAsync(ct);
        if (string.IsNullOrWhiteSpace(token))
        {
            token = await auth.RefreshAccessTokenAsync(ct);
            if (string.IsNullOrEmpty(token))
            {
                DevLogger.Log(GetType().Name, $"{method} {path}: token 缺失且 Refresh 失败");
                return null;
            }
        }

        var resp = await SendCoreAsync(serverUrl, token, method, path, body, ct, swallowNonSuccess: false);
        if (resp is null && string.IsNullOrEmpty(await auth.GetAccessTokenAsync(ct)))
        {
            var newToken = await auth.RefreshAccessTokenAsync(ct);
            if (!string.IsNullOrEmpty(newToken))
            {
                resp = await SendCoreAsync(serverUrl, newToken, method, path, body, ct, swallowNonSuccess: false);
            }
        }
        return resp;
    }

    /// <summary>使用显式 token 发送（用于暂未持久化 token 的多步流程，如登录验证码验证）。</summary>
    protected static async Task<HttpResponseMessage?> SendWithTokenAsync(
        string serverUrl, string token,
        HttpMethod method, string path, string? body, CancellationToken ct)
        => await SendCoreAsync(serverUrl, token, method, path, body, ct, swallowNonSuccess: true);

    private static async Task<HttpResponseMessage?> SendCoreAsync(
        string serverUrl, string token,
        HttpMethod method, string path, string? body, CancellationToken ct,
        bool swallowNonSuccess = true)
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
                // 401：删除 AccessToken（不删 RefreshToken，外层可尝试 Refresh）
                _ = ServiceProvider.Instance.AuthService.InvalidateAccessTokenAsync(ct);
                DevLogger.Log("ApiClient", $"{method} {path}: 401 Unauthorized, AccessToken invalidated");
                return null;
            }
            if (!resp.IsSuccessStatusCode)
            {
                // swallowNonSuccess=true: 旧逻辑，记日志后吞掉错误返回 null
                // swallowNonSuccess=false: 保留响应对象，让调用方读取业务错误体（msg/code）
                if (swallowNonSuccess)
                {
                    var text = await resp.Content.ReadAsStringAsync(ct);
                    DevLogger.Log("ApiClient", $"{method} {path} fail: {(int)resp.StatusCode} {text}");
                    resp.Dispose();
                    return null;
                }
                DevLogger.Log("ApiClient", $"{method} {path} fail (保留响应): {(int)resp.StatusCode}");
            }
            return resp;
        }
        catch (Exception ex)
        {
            DevLogger.Log("ApiClient", ex);
            return null;
        }
    }

    /// <summary>从错误响应的 {state,msg,code} 信封中提取 msg 和 code 字段。</summary>
    protected static async Task<(string msg, string? code)> ReadErrorAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try
        {
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var msg = doc.RootElement.TryGetProperty("msg", out var m) ? m.GetString() ?? "请求失败" : "请求失败";
            var code = doc.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
            return (msg, code);
        }
        catch
        {
            return ($"请求失败 ({(int)resp.StatusCode})", null);
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
    /// 401 仍会清 AccessToken，并抛 <see cref="SyncException"/>（Kind=Auth）。
    /// </summary>
    protected static async Task<HttpResponseMessage> SendWithTokenV2Async(
        string serverUrl, string token,
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
            // 401：删除 AccessToken（不删 RefreshToken，外层 EnsureTokenAsync 可尝试 Refresh）
            _ = ServiceProvider.Instance.AuthService.InvalidateAccessTokenAsync(ct);
            DevLogger.Log("ApiClient", $"{method} {path}: 401 Unauthorized, AccessToken invalidated");
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
