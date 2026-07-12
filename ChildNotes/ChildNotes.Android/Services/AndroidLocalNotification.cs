using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using ChildNotes.Services.Push;

namespace ChildNotes.Android.Services;

/// <summary>
/// Android 本地通知实现：用 AlarmManager 精准调度 + BroadcastReceiver 触发 NotificationManager 弹通知。
///
/// 设计要点：
/// - AlarmManager.setExactAndAllowWhileIdle：API 23+ 可在 Doze 模式下准点触发（需 SCHEDULE_EXACT_ALARM）。
///   若 Android 12+ (API 31+) 不允许 exact alarm，降级为 setAndAllowWhileIdle（不保证精确但能触发）。
/// - PendingIntent + Intent.Extras：携带通知 id/title/body，由 NotificationReceiver 接收后弹出。
/// - requestCode 唯一性：用 id 字符串的 GetHashCode 作为 requestCode，确保不同通知可独立取消。
/// - NotificationChannel：Android 8.0+ 必需，在 MainActivity.OnCreate 中创建（见 ChannelId 常量）。
/// - 权限申请：Android 13+ (API 33+) 需运行时申请 POST_NOTIFICATIONS。
///
/// 与 ServiceProvider 的协作：
/// - ServiceProvider 用条件编译在 Android 平台注入此实例；其他平台注入 NullLocalNotification。
/// - 业务代码调用 ScheduleAsync/CancelAsync 时无需关心平台差异。
/// </summary>
public sealed class AndroidLocalNotification : ILocalNotification
{
    /// <summary>
    /// 通知渠道 ID：Android 8.0+ 通知必须归属某个渠道。
    /// 在 MainActivity.OnCreate 中创建，importance=Default（有声音但不过度打扰）。
    /// </summary>
    public const string ChannelId = "childnotes_reminder";

    /// <summary>
    /// 渠道显示名（用户在系统设置中可见）。
    /// </summary>
    public const string ChannelName = "育儿提醒";

    /// <summary>
    /// 渠道描述：在系统设置中展示给用户看的说明。
    /// </summary>
    public const string ChannelDescription = "喂奶/睡眠/签到等本地提醒通知";

    /// <summary>应用包名前缀，用于 Intent action 命名空间隔离。</summary>
    private const string ActionPrefix = "com.babydiary.app.LOCAL_NOTIFY_";

    private static bool? _permissionGranted;

    /// <summary>
    /// 是否受支持：Android 平台始终受支持。
    /// </summary>
    public bool IsSupported => true;

    /// <summary>
    /// 请求 POST_NOTIFICATIONS 权限（Android 13+ 必需）。
    /// Android 12 及以下版本无需运行时申请，直接返回 true。
    /// </summary>
    /// <returns>true 表示已获得权限或无需申请。</returns>
    public Task<bool> RequestPermissionAsync()
    {
        // Android 12 (API 31) 及以下版本：通知权限是安装时授予的，无需运行时申请
        if ((int)Build.VERSION.SdkInt < 33)
        {
            _permissionGranted = true;
            return Task.FromResult(true);
        }

        // Android 13+ (API 33+)：需运行时申请 POST_NOTIFICATIONS
        // 注意：CheckSelfPermission 用应用级 Context 即可，无需 Activity
        var ctx = Android.App.Application.Context;
        var granted = ctx.CheckSelfPermission(Manifest.Permission.PostNotifications) == Permission.Granted;
        _permissionGranted = granted;
        return Task.FromResult(granted);
    }

    /// <summary>
    /// 调度一条本地通知：用 AlarmManager 在指定时间触发 BroadcastReceiver，由 receiver 弹通知。
    /// </summary>
    /// <param name="id">通知唯一标识（用于取消；也作为 Intent action 后缀，需稳定不变）。</param>
    /// <param name="title">通知标题。</param>
    /// <param name="body">通知正文。</param>
    /// <param name="fireAt">触发时间（本地时间）。</param>
    /// <param name="data">附加数据（点击跳转用，当前未使用，预留）。</param>
    public Task ScheduleAsync(string id, string title, string body, DateTime fireAt,
        IReadOnlyDictionary<string, string>? data = null)
    {
        try
        {
            // 应用级 Context：始终可用，AlarmManager/NotificationManager 都可用它获取
            var ctx = Android.App.Application.Context;
            var alarmMgr = AlarmManager.FromContext(ctx);

            // 先取消同 id 的旧 PendingIntent（保证 Schedule 的重复调用是"覆盖"语义）
            // NoCreate 模式：若不存在返回 null，AlarmManager.Cancel(null) 是安全的 no-op
            var oldPending = BuildPendingIntent(ctx, id, title, body, update: false);
            if (oldPending is not null)
            {
                alarmMgr?.Cancel(oldPending);
            }

            // 触发时间：转为 UTC 毫秒；若已过期则延后 1 秒触发（避免立即触发过于突兀，也方便测试）
            var triggerAtMillis = ToEpochMillis(fireAt);
            if (triggerAtMillis < Java.Lang.JavaSystem.CurrentTimeMillis())
            {
                triggerAtMillis = Java.Lang.JavaSystem.CurrentTimeMillis() + 1000;
            }

            // 创建新 PendingIntent（UpdateCurrent 模式：存在则更新 extras，不存在则创建）
            var pending = BuildPendingIntent(ctx, id, title, body, update: true);
            if (pending is null)
            {
                Android.Util.Log.Warn("ChildNotes", $"[LocalNoti] Failed to create PendingIntent for id={id}");
                return Task.CompletedTask;
            }

            if (alarmMgr is not null)
            {
                try
                {
                    // setExactAndAllowWhileIdle：在 Doze 模式下也能准点触发
                    // API 31+ 需 SCHEDULE_EXACT_ALARM 权限（manifest 已声明 USE_EXACT_ALARM 作为兜底）
                    alarmMgr.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, triggerAtMillis, pending);
                }
                catch (Java.Lang.SecurityException)
                {
                    // 用户撤销了 SCHEDULE_EXACT_ALARM 权限（Android 12+）：降级为非精确闹钟
                    // 非精确闹钟会在 Doze 窗口内延迟触发（最多延迟 15 分钟），但至少能触发
                    alarmMgr.SetAndAllowWhileIdle(AlarmType.RtcWakeup, triggerAtMillis, pending);
                }
            }

            Android.Util.Log.Info("ChildNotes", $"[LocalNoti] Scheduled id={id} title={title} fireAt={fireAt:O}");
        }
        catch (Exception ex)
        {
            Android.Util.Log.Error("ChildNotes", $"[LocalNoti] ScheduleAsync failed: {ex}");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 取消指定 ID 的通知：取消 AlarmManager 的 PendingIntent，并取消已弹出的通知。
    /// </summary>
    public Task CancelAsync(string id)
    {
        try
        {
            // 应用级 Context：始终可用，AlarmManager/NotificationManager 都可用它获取
            var ctx = Android.App.Application.Context;
            var alarmMgr = AlarmManager.FromContext(ctx);
            // NoCreate 模式：若不存在返回 null（说明从未 schedule 过，无需取消）
            var pending = BuildPendingIntent(ctx, id, "", "", update: false);
            if (pending is not null)
            {
                alarmMgr?.Cancel(pending);
            }

            // 取消已弹出的通知（若已触发但用户未点掉）
            var notifMgr = NotificationManager.FromContext(ctx);
            notifMgr?.Cancel(id.GetHashCode());

            Android.Util.Log.Info("ChildNotes", $"[LocalNoti] Cancelled id={id}");
        }
        catch (Exception ex)
        {
            Android.Util.Log.Error("ChildNotes", $"[LocalNoti] CancelAsync failed: {ex}");
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 取消所有本地通知：遍历所有已 schedule 的 id 不现实（无全局注册表），
    /// 这里通过 Intent 匹配 action 前缀全部取消 AlarmManager。
    /// 由于我们用 action 命名空间隔离，可用 PendingIntent.FLAG_NO_CREATE 探测取消。
    /// 简化实现：调用 NotificationManager.CancelAll 取消已弹出的，AlarmManager 的 future 取消需业务层维护 id 列表。
    /// </summary>
    public Task CancelAllAsync()
    {
        try
        {
            // 应用级 Context：始终可用，AlarmManager/NotificationManager 都可用它获取
            var ctx = Android.App.Application.Context;
            var notifMgr = NotificationManager.FromContext(ctx);
            notifMgr?.CancelAll();
            Android.Util.Log.Info("ChildNotes", "[LocalNoti] CancelAll (shown only)");
        }
        catch (Exception ex)
        {
            Android.Util.Log.Error("ChildNotes", $"[LocalNoti] CancelAllAsync failed: {ex}");
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 构建 PendingIntent：封装 BroadcastReceiver 的触发 intent。
    /// requestCode 用 id.GetHashCode 保证唯一性，便于后续 Cancel。
    /// </summary>
    /// <param name="update">true 时创建/更新 PendingIntent；false 时用 FLAG_NO_CREATE 仅查询已存在的（用于 Cancel，不存在返回 null）。</param>
    private static PendingIntent? BuildPendingIntent(Context ctx, string id, string title, string body, bool update)
    {
        var intent = new Intent(ctx, typeof(NotificationReceiver));
        intent.SetAction(ActionPrefix + id);
        intent.PutExtra(NotificationReceiver.ExtraId, id);
        intent.PutExtra(NotificationReceiver.ExtraTitle, title);
        intent.PutExtra(NotificationReceiver.ExtraBody, body);

        // 基础 flags：update=true 用 UpdateCurrent（不存在则创建），update=false 用 NoCreate（不存在返回 null）
        var flags = update ? PendingIntentFlags.UpdateCurrent : PendingIntentFlags.NoCreate;
        // Android 12+ (API 31+)：强制要求 FLAG_IMMUTABLE 或 FLAG_MUTABLE；本 intent 无需后续修改，用 IMMUTABLE
        if ((int)Build.VERSION.SdkInt >= 31)
        {
            flags |= PendingIntentFlags.Immutable;
        }

        return PendingIntent.GetBroadcast(ctx, id.GetHashCode(), intent, flags);
    }

    /// <summary>本地时间 → UTC 毫秒（AlarmType.RtcWakeup 用绝对时间）。</summary>
    private static long ToEpochMillis(DateTime localTime)
    {
        // Android 的 System.currentTimeMillis() 是 UTC 毫秒；DateTime 需指定 Kind=Local
        var dt = localTime.Kind == DateTimeKind.Utc ? localTime : localTime.ToUniversalTime();
        return new DateTimeOffset(dt, TimeSpan.Zero).ToUnixTimeMilliseconds();
    }
}
