using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using ChildNotes.Services.Push;
using Application = Android.App.Application;  // 别名：避免与 ChildNotes.Android.Application 歧义
using Log = Android.Util.Log;                 // 别名：避免与 ChildNotes.Android.Util 歧义

namespace ChildNotes.Android.Services;

/// <summary>
/// Android 本地通知实现：用 AlarmManager 精准调度 + BroadcastReceiver 触发 NotificationManager 弹通知。
///
/// 命名空间冲突说明：
/// 本项目命名空间为 ChildNotes.Android.*，会遮蔽全局 Android.* 命名空间。
/// 解决方案：用 using 别名（Application/Log）+ 直接用已 using 的短名（AlarmManager/Context/Intent 等），
/// 避免 Android.App.X / Android.Util.X 完整路径被解析为 ChildNotes.Android.App.X。
///
/// 设计要点：
/// - AlarmManager.setExactAndAllowWhileIdle：API 23+ 可在 Doze 模式下准点触发（需 SCHEDULE_EXACT_ALARM）。
///   若 Android 12+ (API 31+) 不允许 exact alarm，降级为 setAndAllowWhileIdle（不保证精确但能触发）。
/// - PendingIntent + Intent.Extras：携带通知 id/title/body，由 NotificationReceiver 接收后弹出。
/// - requestCode 唯一性：用 id 字符串的 GetHashCode 作为 requestCode，确保不同通知可独立取消。
/// - NotificationChannel：Android 8.0+ 必需，在 MainActivity.OnCreate 中创建（见 ChannelId 常量）。
/// - 权限申请：Android 13+ (API 33+) 需运行时申请 POST_NOTIFICATIONS。
/// </summary>
public sealed class AndroidLocalNotification : ILocalNotification
{
    /// <summary>通知渠道 ID：Android 8.0+ 通知必须归属某个渠道。</summary>
    public const string ChannelId = "childnotes_reminder";

    /// <summary>渠道显示名（用户在系统设置中可见）。</summary>
    public const string ChannelName = "育儿提醒";

    /// <summary>渠道描述：在系统设置中展示给用户看的说明。</summary>
    public const string ChannelDescription = "喂奶/睡眠/签到等本地提醒通知";

    /// <summary>应用包名前缀，用于 Intent action 命名空间隔离。</summary>
    private const string ActionPrefix = "com.babydiary.app.LOCAL_NOTIFY_";

    /// <summary>
    /// 是否受支持：Android 平台始终受支持。
    /// </summary>
    public bool IsSupported => true;

    /// <summary>
    /// 请求 POST_NOTIFICATIONS 权限（Android 13+ 必需）。
    /// Android 12 及以下版本无需运行时申请，直接返回 true。
    /// </summary>
    public Task<bool> RequestPermissionAsync()
    {
        // Android 12 (API 31) 及以下版本：通知权限是安装时授予的，无需运行时申请
        if ((int)Build.VERSION.SdkInt < 33)
        {
            return Task.FromResult(true);
        }

        // Android 13+ (API 33+)：需运行时申请 POST_NOTIFICATIONS
        // 注意：CheckSelfPermission 用应用级 Context 即可，无需 Activity
        var ctx = Application.Context;
        // 用字符串常量 "android.permission.POST_NOTIFICATIONS" 替代 Manifest.Permission.PostNotifications
        // 避免 Manifest 类在命名空间冲突下无法解析
        var granted = ctx.CheckSelfPermission("android.permission.POST_NOTIFICATIONS") == (int)Permission.Granted;
        return Task.FromResult(granted);
    }

    /// <summary>
    /// 调度一条本地通知：用 AlarmManager 在指定时间触发 BroadcastReceiver，由 receiver 弹通知。
    /// </summary>
    public Task ScheduleAsync(string id, string title, string body, DateTime fireAt,
        IReadOnlyDictionary<string, string>? data = null)
    {
        try
        {
            var ctx = Application.Context;
            var alarmMgr = AlarmManager.FromContext(ctx);

            // 先取消同 id 的旧 PendingIntent（保证 Schedule 的重复调用是"覆盖"语义）
            var oldPending = BuildPendingIntent(ctx, id, title, body, update: false);
            if (oldPending is not null)
            {
                alarmMgr?.Cancel(oldPending);
            }

            // 触发时间：转为 UTC 毫秒；若已过期则延后 1 秒触发
            var triggerAtMillis = ToEpochMillis(fireAt);
            if (triggerAtMillis < Java.Lang.JavaSystem.CurrentTimeMillis())
            {
                triggerAtMillis = Java.Lang.JavaSystem.CurrentTimeMillis() + 1000;
            }

            var pending = BuildPendingIntent(ctx, id, title, body, update: true);
            if (pending is null)
            {
                Log.Warn("ChildNotes", $"[LocalNoti] Failed to create PendingIntent for id={id}");
                return Task.CompletedTask;
            }

            if (alarmMgr is not null)
            {
                try
                {
                    // setExactAndAllowWhileIdle：在 Doze 模式下也能准点触发
                    alarmMgr.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, triggerAtMillis, pending);
                }
                catch (Java.Lang.SecurityException)
                {
                    // 用户撤销了 SCHEDULE_EXACT_ALARM 权限（Android 12+）：降级为非精确闹钟
                    alarmMgr.SetAndAllowWhileIdle(AlarmType.RtcWakeup, triggerAtMillis, pending);
                }
            }

            Log.Info("ChildNotes", $"[LocalNoti] Scheduled id={id} title={title} fireAt={fireAt:O}");
        }
        catch (Exception ex)
        {
            Log.Error("ChildNotes", $"[LocalNoti] ScheduleAsync failed: {ex}");
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
            var ctx = Application.Context;
            var alarmMgr = AlarmManager.FromContext(ctx);
            var pending = BuildPendingIntent(ctx, id, "", "", update: false);
            if (pending is not null)
            {
                alarmMgr?.Cancel(pending);
            }

            // 取消已弹出的通知（若已触发但用户未点掉）
            var notifMgr = NotificationManager.FromContext(ctx);
            notifMgr?.Cancel(id.GetHashCode());

            Log.Info("ChildNotes", $"[LocalNoti] Cancelled id={id}");
        }
        catch (Exception ex)
        {
            Log.Error("ChildNotes", $"[LocalNoti] CancelAsync failed: {ex}");
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 取消所有已弹出的本地通知。
    /// 注意：AlarmManager 的 future 取消需业务层维护 id 列表逐一 CancelAsync，
    /// 此方法只取消通知栏已显示的（用于登出等场景）。
    /// </summary>
    public Task CancelAllAsync()
    {
        try
        {
            var ctx = Application.Context;
            var notifMgr = NotificationManager.FromContext(ctx);
            notifMgr?.CancelAll();
            Log.Info("ChildNotes", "[LocalNoti] CancelAll (shown only)");
        }
        catch (Exception ex)
        {
            Log.Error("ChildNotes", $"[LocalNoti] CancelAllAsync failed: {ex}");
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
        // Android 12+ (API 31+)：强制要求 FLAG_IMMUTABLE 或 FLAG_MUTABLE
        if ((int)Build.VERSION.SdkInt >= 31)
        {
            flags |= PendingIntentFlags.Immutable;
        }

        return PendingIntent.GetBroadcast(ctx, id.GetHashCode(), intent, flags);
    }

    /// <summary>本地时间 → UTC 毫秒（AlarmType.RtcWakeup 用绝对时间）。</summary>
    private static long ToEpochMillis(DateTime localTime)
    {
        var dt = localTime.Kind == DateTimeKind.Utc ? localTime : localTime.ToUniversalTime();
        return new DateTimeOffset(dt, TimeSpan.Zero).ToUnixTimeMilliseconds();
    }
}
