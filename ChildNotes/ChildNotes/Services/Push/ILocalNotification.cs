namespace ChildNotes.Services.Push;

/// <summary>
/// 本地通知抽象：不依赖后端推送，由应用本地调度。
///
/// 适用场景：
/// - 每日签到提醒
/// - 喂奶间隔超时提醒（如 3 小时未记录喂奶）
/// - 睡眠超时提醒（如 4 小时未结束睡眠）
/// - 疫苗接种日提醒
///
/// 各平台实现：
/// - Android：NotificationManager + AlarmManager（或 WorkManager）
/// - iOS：UNUserNotificationCenter
/// - Desktop：NullLocalNotification（桌面端无需本地通知）
///
/// 当前状态：接口已预留，未实现具体平台。后续启用时实现平台类即可。
/// </summary>
public interface ILocalNotification
{
    /// <summary>是否受支持（Desktop 返回 false）。</summary>
    bool IsSupported { get; }

    /// <summary>请求通知权限（Android 13+ 需运行时申请 POST_NOTIFICATIONS）。</summary>
    /// <returns>true 表示已获得权限。</returns>
    Task<bool> RequestPermissionAsync();

    /// <summary>
    /// 调度一条本地通知。
    /// </summary>
    /// <param name="id">通知唯一标识（用于取消）。</param>
    /// <param name="title">标题。</param>
    /// <param name="body">正文。</param>
    /// <param name="fireAt">触发时间（本地时间）。</param>
    /// <param name="data">附加数据（点击跳转用）。</param>
    Task ScheduleAsync(string id, string title, string body, DateTime fireAt, IReadOnlyDictionary<string, string>? data = null);

    /// <summary>取消指定 ID 的通知。</summary>
    Task CancelAsync(string id);

    /// <summary>取消所有本地通知。</summary>
    Task CancelAllAsync();
}
