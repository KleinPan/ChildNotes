namespace ChildNotes.Services.Push;

/// <summary>
/// 后端推送服务的客户端镜像接口：用于向后端注册/注销设备 token、查询未读消息等。
///
/// 实际推送发送由后端 PushController 触发，本接口只负责设备 token 管理。
/// 后端接口尚未实现，此接口为预留契约，便于后续前后端联调。
/// </summary>
public interface IPushService
{
    /// <summary>注册设备 token 到后端（登录后调用）。</summary>
    /// <param name="token">推送 token（FCM/APNs/厂商 token）。</param>
    /// <param name="platformId">平台标识（见 <see cref="IPushPlatform.PlatformId"/>）。</param>
    Task RegisterTokenAsync(string token, string platformId);

    /// <summary>注销当前设备的 token（登出时调用）。</summary>
    Task UnregisterTokenAsync();
}
