using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ChildNotes.Data.Repositories;
using ChildNotes.Infrastructure;

namespace ChildNotes.Services.Push;

/// <summary>
/// 后端推送 API 的默认实现：调用 /api/push/register-token 与 /api/push/unregister-token。
///
/// 当前后端接口尚未实现，调用失败时静默吞掉异常（推送为辅助功能，不应阻塞主流程）。
/// 后端 PushController 实现后，本类无需修改即可正常工作。
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
            if (string.IsNullOrWhiteSpace(serverUrl) || string.IsNullOrWhiteSpace(cfg.Token))
            {
                DevLogger.Log("Push", $"RegisterToken skipped: serverUrl/token empty");
                return;
            }

            var url = $"{serverUrl.TrimEnd('/')}/api/push/register-token";
            var body = JsonSerializer.Serialize(new { token, platform = platformId });
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.Token);
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
            if (string.IsNullOrWhiteSpace(serverUrl) || string.IsNullOrWhiteSpace(cfg.Token)) return;

            var url = $"{serverUrl.TrimEnd('/')}/api/push/unregister-token";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.Token);
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
