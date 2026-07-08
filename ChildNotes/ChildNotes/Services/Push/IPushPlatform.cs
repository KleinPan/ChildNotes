namespace ChildNotes.Services.Push;

/// <summary>
/// 推送平台抽象：跨平台推送 token 注册与消息接收。
///
/// 各平台实现：
/// - Android：FCM（海外）/ 华为 HMS / 小米 MiPush / OPPO HeytapPush / Vivo VPush
/// - iOS：APNs
/// - Desktop：无推送（NullPushPlatform 空实现）
///
/// 集成步骤：
/// 1. 平台项目实现 IPushPlatform，在 ServiceProvider 初始化时注入
/// 2. App 启动后调用 InitializeAsync() 获取 token 并上报后端
/// 3. 订阅 NotificationReceived / NotificationTapped 事件处理消息
///
/// 当前状态：仅有 NullPushPlatform 空实现，未实际接入任何推送 SDK。
/// 后续接入国内厂商推送时，无需修改业务代码，只需替换平台实现。
/// </summary>
public interface IPushPlatform
{
    /// <summary>平台标识：android-fcm / android-hms / android-mi / android-oppo / android-vivo / ios / desktop</summary>
    string PlatformId { get; }

    /// <summary>是否实际可用（NullPushPlatform 返回 false）。</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// 初始化推送 SDK 并获取 token。
    /// 实现应异步等待 SDK 注册完成，token 拿到后通过 PushApiClient 上报后端。
    /// </summary>
    Task InitializeAsync();

    /// <summary>当前 token（已注册后可读取，未注册返回 null）。</summary>
    string? CurrentToken { get; }

    /// <summary>收到推送消息（应用在前台时触发）。</summary>
    /// <param name="title">通知标题。</param>
    /// <param name="body">通知正文。</param>
    /// <param name="data">附加数据（用于点击跳转路由）。</param>
    event Action<string, string?, IReadOnlyDictionary<string, string>?>? NotificationReceived;

    /// <summary>用户点击通知打开应用时触发（携带附加数据，用于跳转到对应页面）。</summary>
    event Action<IReadOnlyDictionary<string, string>>? NotificationTapped;
}
