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

    // 共享 JSON 序列化选项（收口到 ApiEnvelope 统一维护，行为不变）
    protected static readonly JsonSerializerOptions JsonOpts = ApiEnvelope.JsonOpts;

    /// <summary>
    /// 使用 SecureStorage 中的 AccessToken 发送请求。
    /// 401 时自动尝试 RefreshToken Rotation；仍失败则返回 null（登录态已清空，业务数据保留）。
    /// </summary>
    protected async Task<HttpResponseMessage?> SendAsync(
        SyncConfigRepository cfgRepo,
        HttpMethod method, string path, string? body, CancellationToken ct,
        IReadOnlyDictionary<string, string>? extraHeaders = null)
        => await SendWithAuthAsync(cfgRepo, method, path, body, ct, swallowNonSuccess: true, extraHeaders);

    /// <summary>
    /// 与 <see cref="SendAsync"/> 行为一致，但非 2xx 响应会返回 <see cref="HttpResponseMessage"/>
    /// 而非 null，供调用方读取后端业务错误体（{state,msg,data} 信封中的 msg/code）。
    /// 仅以下情况返回 null：token 缺失且 Refresh 失败、网络异常、401 重试仍失败。
    /// </summary>
    protected async Task<HttpResponseMessage?> SendWithErrorAsync(
        SyncConfigRepository cfgRepo,
        HttpMethod method, string path, string? body, CancellationToken ct)
        => await SendWithAuthAsync(cfgRepo, method, path, body, ct, swallowNonSuccess: false, null);

    /// <summary>
    /// SendAsync/SendWithErrorAsync 的共享核心（两方法 90% 重复收口）：
    /// ServerUrl 回退、token 获取（含 JWT exp 预检）、401 Refresh 重试。
    /// 差异仅在 swallowNonSuccess：非 2xx 是否返回响应对象供调用方读取业务错误体。
    /// </summary>
    private async Task<HttpResponseMessage?> SendWithAuthAsync(
        SyncConfigRepository cfgRepo,
        HttpMethod method, string path, string? body, CancellationToken ct,
        bool swallowNonSuccess, IReadOnlyDictionary<string, string>? extraHeaders)
    {
        var cfg = cfgRepo.Get();
        // ServerUrl 为空回退默认地址：默认 DB 里 server_url 是空串，
        // 此前"提前 return"会让 5 个 API 客户端在默认配置下全部失效（修复：删除短路）
        var serverUrl = string.IsNullOrWhiteSpace(cfg.ServerUrl)
            ? ServerEndpoints.Primary
            : cfg.ServerUrl!;

        var token = await GetUsableAccessTokenAsync(ct);
        if (string.IsNullOrEmpty(token))
        {
            DevLogger.Log(GetType().Name, $"{method} {path}: token 缺失且 Refresh 失败");
            return null;
        }

        var resp = await SendCoreAsync(serverUrl, token, method, path, body, ct, swallowNonSuccess, extraHeaders);
        // 401 时 SendCoreAsync 已删除 AccessToken，这里尝试 Refresh 续期重试一次
        var auth = ServiceProvider.Instance.AuthService;
        if (resp is null && string.IsNullOrEmpty(await auth.GetAccessTokenAsync(ct)))
        {
            var newToken = await auth.RefreshAccessTokenAsync(ct);
            if (!string.IsNullOrEmpty(newToken))
            {
                resp = await SendCoreAsync(serverUrl, newToken, method, path, body, ct, swallowNonSuccess, extraHeaders);
            }
        }
        return resp;
    }

    /// <summary>
    /// 获取可用 AccessToken：缺失或 JWT exp 已过期时尝试 RefreshToken 续期。
    /// BaseApiClient 发送路径与 UploadService 共享（收口第三份手写 token 获取）。
    /// </summary>
    internal static async Task<string?> GetUsableAccessTokenAsync(CancellationToken ct)
    {
        var auth = ServiceProvider.Instance.AuthService;
        var token = await auth.GetAccessTokenAsync(ct);
        if (!string.IsNullOrWhiteSpace(token) && !IsJwtExpired(token))
            return token;
        return await auth.RefreshAccessTokenAsync(ct);
    }

    /// <summary>
    /// 轻量 JWT exp 解码：手写 Base64Url 解码 + 字符串查找 exp claim。
    /// 不引入 JWT 库，兼容 AOT/Trimming。解析失败返回 false（不拦截，让 401 处理）。
    /// 从 ApiSyncService 上移共享（所有客户端 token 获取统一预检，避免白吃一次 401 往返）。
    /// </summary>
    protected static bool IsJwtExpired(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2) return false;
            var payload = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
            // 简单查找 "exp":1234567890（Unix 秒）
            var key = "\"exp\"";
            var idx = payload.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0) return false;
            idx += key.Length;
            while (idx < payload.Length && (payload[idx] == ':' || payload[idx] == ' ')) idx++;
            var start = idx;
            while (idx < payload.Length && char.IsDigit(payload[idx])) idx++;
            if (idx <= start) return false;
            var exp = long.Parse(payload.AsSpan(start, idx - start));
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return now >= exp;
        }
        catch
        {
            return false;
        }
    }

    private static byte[] Base64UrlDecode(string s)
    {
        var sb = new StringBuilder(s);
        sb.Replace('-', '+').Replace('_', '/');
        switch (sb.Length % 4)
        {
            case 2: sb.Append("=="); break;
            case 3: sb.Append("="); break;
        }
        return Convert.FromBase64String(sb.ToString());
    }

    /// <summary>使用显式 token 发送（用于暂未持久化 token 的多步流程，如登录验证码验证）。</summary>
    protected static async Task<HttpResponseMessage?> SendWithTokenAsync(
        string serverUrl, string token,
        HttpMethod method, string path, string? body, CancellationToken ct)
        => await SendCoreAsync(serverUrl, token, method, path, body, ct, swallowNonSuccess: true);

    private static async Task<HttpResponseMessage?> SendCoreAsync(
        string serverUrl, string token,
        HttpMethod method, string path, string? body, CancellationToken ct,
        bool swallowNonSuccess = true,
        IReadOnlyDictionary<string, string>? extraHeaders = null)
    {
        var url = serverUrl.TrimEnd('/') + path;
        using var req = new HttpRequestMessage(method, url);
        if (body is not null)
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (extraHeaders is not null)
        {
            foreach (var (name, value) in extraHeaders)
                req.Headers.TryAddWithoutValidation(name, value);
        }
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
                    // 错误响应体可能很大（如 HTML 错误页），截断到 500 字符避免日志膨胀
                    if (text.Length > 500) text = text[..500] + "…(truncated)";
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

    /// <summary>从错误响应的 {state,msg,code} 信封中提取 msg 和 code 字段（共享实现见 ApiEnvelope）。</summary>
    protected static Task<(string msg, string? code)> ReadErrorAsync(HttpResponseMessage resp, CancellationToken ct)
        => ApiEnvelope.ReadErrorAsync(resp, ct);

    /// <summary>从 {state,msg,data} 信封中提取 data 字段并反序列化（共享实现见 ApiEnvelope）。</summary>
    protected static T? ExtractData<T>(string json)
        => ApiEnvelope.ExtractData<T>(json, "ApiClient");

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
