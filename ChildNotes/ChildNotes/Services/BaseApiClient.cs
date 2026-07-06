using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChildNotes.Data.Repositories;
using ChildNotes.Infrastructure;
using ChildNotes.Models;

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
        // 后端 ASP.NET Core 默认 camelCase 序列化（serverTime/expireAt 等），
        // 前端 DTO 用 PascalCase（ServerTime/ExpireAt）。开启大小写不敏感，
        // 避免字段名大小写不匹配导致 DateTime 等类型用默认值（0001-01-01）。
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>使用配置中的 ServerUrl/Token 发送请求；401 时自动清空 token 并返回 null。</summary>
    /// <remarks>
    /// 与仅做"有 token 就发、没有就放弃"的早期实现不同，现在 token 为空时会尝试用
    /// sync_config 中的 username/password 自动登录换取 token，避免同步流程已登录、
    /// 但其它在线功能（如家人管理）因读到的缓存 token 仍为空而报"未配置"。
    /// 登录失败仍返回 null，由调用方决定如何提示。
    /// </remarks>
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
        var token = cfg.Token;
        if (string.IsNullOrWhiteSpace(token))
        {
            token = await TryLoginAsync(cfgRepo, cfg, ct);
            if (string.IsNullOrEmpty(token))
            {
                DevLogger.Log(GetType().Name, $"{method} {path}: token 未配置且自动登录失败");
                return null;
            }
        }
        var resp = await SendCoreAsync(cfgRepo, cfg.ServerUrl!, token, method, path, body, ct);
        // 401 时 SendCoreAsync 已清空 token，这里再尝试重新登录重试一次，
        // 覆盖"token 在 DB 中已过期、但本地缓存还在用旧值"的场景。
        if (resp is null && string.IsNullOrEmpty(cfgRepo.Get().Token))
        {
            var newToken = await TryLoginAsync(cfgRepo, cfg, ct);
            if (!string.IsNullOrEmpty(newToken))
            {
                resp = await SendCoreAsync(cfgRepo, cfg.ServerUrl!, newToken, method, path, body, ct);
            }
        }
        return resp;
    }

    /// <summary>
    /// 用 sync_config 中的 username/password 向服务器登录换取 token，
    /// 成功后写回 DB（UpdateToken）并返回 token；失败返回 null。
    /// 供 <see cref="SendAsync"/> 在 token 缺失时自动补救，以及派生类按需调用。
    /// </summary>
    protected static async Task<string?> TryLoginAsync(
        SyncConfigRepository cfgRepo, SyncConfig cfg, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cfg.Username) || string.IsNullOrWhiteSpace(cfg.Password))
            return null;

        var serverUrl = string.IsNullOrWhiteSpace(cfg.ServerUrl)
            ? ServerEndpoints.Primary
            : cfg.ServerUrl!;
        var url = serverUrl.TrimEnd('/') + "/api/auth/login";
        var body = Serialize(new { username = cfg.Username, password = cfg.Password });
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        try
        {
            using var resp = await Http.SendAsync(req, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                DevLogger.Log("ApiClient", $"Auto-login fail: {(int)resp.StatusCode} {json}");
                return null;
            }
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("token", out var tokenEl) ||
                string.IsNullOrWhiteSpace(tokenEl.GetString()))
            {
                DevLogger.Log("ApiClient", "Auto-login fail: 响应缺少 data.token");
                return null;
            }
            var token = tokenEl.GetString()!;
            cfgRepo.UpdateToken(token);
            DevLogger.Log("ApiClient", "Auto-login ok, token updated");
            return token;
        }
        catch (Exception ex)
        {
            DevLogger.Log("ApiClient", "Auto-login exception: " + ex.Message);
            return null;
        }
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
