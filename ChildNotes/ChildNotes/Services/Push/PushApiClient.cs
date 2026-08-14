using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ChildNotes.Data.Repositories;
using ChildNotes.Infrastructure;

namespace ChildNotes.Services.Push;

/// <summary>
/// 后端推送 API 的默认实现：调用 /api/push/register-token 与 /api/push/unregister-token。
///
/// v5：AccessToken 从 ISecureStorage 读取（非明文 SQLite）；缺失时尝试 RefreshToken 续期。
/// 后端接口未实现时静默吞掉异常（推送为辅助功能，不应阻塞主流程）。
/// </summary>
public sealed class PushApiClient : IPushService
{
    private readonly SyncConfigRepository _cfgRepo;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public PushApiClient(SyncConfigRepository cfgRepo)
    {
        _cfgRepo = cfgRepo;
    }

    public async Task RegisterTokenAsync(string token, string platformId)
    {
        try
        {
            var cfg = _cfgRepo.Get();
            var serverUrl = cfg.ServerUrl;
            if (string.IsNullOrWhiteSpace(serverUrl)) return;

            // v5：从 SecureStorage 读取 AccessToken
            var auth = ServiceProvider.Instance.AuthService;
            var accessToken = await auth.GetAccessTokenAsync();
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                accessToken = await auth.RefreshAccessTokenAsync();
                if (string.IsNullOrEmpty(accessToken))
                {
                    DevLogger.Log("Push", "RegisterToken skipped: token 缺失且 Refresh 失败");
                    return;
                }
            }

            var url = $"{serverUrl.TrimEnd('/')}/api/push/register-token";
            var body = JsonSerializer.Serialize(new { token, platform = platformId });
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            req.Headers.Add("X-Device-Id", cfg.DeviceId ?? string.Empty);

            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                DevLogger.Log("Push", $"RegisterToken failed: {(int)resp.StatusCode} {resp.ReasonPhrase}");
            }
            else
            {
                DevLogger.Log("Push", $"RegisterToken ok: platform={platformId}");
            }
        }
        catch (Exception ex)
        {
            // 后端未实现时返回 404 或连接失败，静默处理
            DevLogger.Log("Push", $"RegisterToken error (backend not ready?): {ex.Message}");
        }
    }

    public async Task UnregisterTokenAsync()
    {
        try
        {
            var cfg = _cfgRepo.Get();
            var serverUrl = cfg.ServerUrl;
            if (string.IsNullOrWhiteSpace(serverUrl)) return;

            var auth = ServiceProvider.Instance.AuthService;
            var accessToken = await auth.GetAccessTokenAsync();
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                accessToken = await auth.RefreshAccessTokenAsync();
                if (string.IsNullOrEmpty(accessToken)) return;
            }

            var url = $"{serverUrl.TrimEnd('/')}/api/push/unregister-token";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            if (!string.IsNullOrEmpty(cfg.DeviceId))
            {
                req.Headers.Add("X-Device-Id", cfg.DeviceId);
            }

            using var resp = await Http.SendAsync(req);
            DevLogger.Log("Push", $"UnregisterToken: {(int)resp.StatusCode}");
        }
        catch (Exception ex)
        {
            DevLogger.Log("Push", $"UnregisterToken error: {ex.Message}");
        }
    }
}
