using ChildNotes.Data.Repositories;
using ChildNotes.Infrastructure;

namespace ChildNotes.Services.Push;

/// <summary>
/// 后端推送 API 的默认实现：调用 /api/push/register-token 与 /api/push/unregister-token。
///
/// v5：AccessToken 从 ISecureStorage 读取（非明文 SQLite）；缺失/过期时尝试 RefreshToken 续期。
/// 继承 BaseApiClient 复用 SendAsync（含 token 获取/401 Refresh 重试）与共享 HttpClient，
/// 删除原自带的静态 HttpClient 与手写 token/refresh 逻辑。
/// 后端接口未实现时静默吞掉异常（推送为辅助功能，不应阻塞主流程）。
/// </summary>
public sealed class PushApiClient : BaseApiClient, IPushService
{
    private readonly SyncConfigRepository _cfgRepo;

    public PushApiClient(SyncConfigRepository cfgRepo)
    {
        _cfgRepo = cfgRepo;
    }

    public async Task RegisterTokenAsync(string token, string platformId)
    {
        try
        {
            var cfg = _cfgRepo.Get();
            // X-Device-Id 头：BaseApiClient 不感知业务头，经 extraHeaders 透传
            var headers = new Dictionary<string, string> { ["X-Device-Id"] = cfg.DeviceId ?? string.Empty };
            using var resp = await SendAsync(_cfgRepo, HttpMethod.Post, "/api/push/register-token",
                Serialize(new { token, platform = platformId }), CancellationToken.None, headers);
            if (resp is not null && resp.IsSuccessStatusCode)
            {
                DevLogger.Log("Push", $"RegisterToken ok: platform={platformId}");
            }
            // 失败已在 BaseApiClient 内记日志；推送为辅助功能，静默处理
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
            Dictionary<string, string>? headers = null;
            if (!string.IsNullOrEmpty(cfg.DeviceId))
            {
                headers = new Dictionary<string, string> { ["X-Device-Id"] = cfg.DeviceId };
            }
            using var resp = await SendAsync(_cfgRepo, HttpMethod.Post, "/api/push/unregister-token",
                null, CancellationToken.None, headers);
            DevLogger.Log("Push", $"UnregisterToken: {(resp is null ? 0 : (int)resp.StatusCode)}");
        }
        catch (Exception ex)
        {
            DevLogger.Log("Push", $"UnregisterToken error: {ex.Message}");
        }
    }
}
