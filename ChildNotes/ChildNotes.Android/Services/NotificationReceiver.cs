using System;
using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using Log = Android.Util.Log;  // 别名：避免与 ChildNotes.Android.Util 冲突

namespace ChildNotes.Android.Services;

/// <summary>
/// AlarmManager 触发的 BroadcastReceiver：收到 pending intent 后弹出通知。
///
/// 生命周期：AlarmManager 在触发时间激活此 Receiver（即使 App 已被杀进程也能唤醒）。
/// onReceive 必须快速完成（< 10 秒），否则会被系统 kill。这里只做 NotificationManager.Notify。
/// </summary>
[BroadcastReceiver(
    Enabled = true,
    Exported = false,
    Name = "com.babydiary.app.NotificationReceiver")]
[IntentFilter(new[] { "com.babydiary.app.LOCAL_NOTIFY" })]
public sealed class NotificationReceiver : BroadcastReceiver
{
    public const string ExtraId = "notif_id";
    public const string ExtraTitle = "notif_title";
    public const string ExtraBody = "notif_body";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null) return;

        try
        {
            var id = intent.GetStringExtra(ExtraId) ?? Guid.NewGuid().ToString();
            var title = intent.GetStringExtra(ExtraTitle) ?? "宝宝日记";
            var body = intent.GetStringExtra(ExtraBody) ?? string.Empty;

            // 构造通知：用 NotificationCompat（兼容 Android 8.0 以下）
            var builder = new NotificationCompat.Builder(context, AndroidLocalNotification.ChannelId)
                .SetSmallIcon(Resource.Drawable.Icon)
                .SetContentTitle(title)
                .SetContentText(body)
                .SetStyle(new NotificationCompat.BigTextStyle().BigText(body))
                .SetAutoCancel(true)
                .SetPriority(NotificationCompat.PriorityDefault);

            // 点击跳转：当前简化为打开 App（Launcher Intent），后续接入 DeepLink 时扩展
            var launchIntent = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName ?? string.Empty);
            if (launchIntent is not null)
            {
                launchIntent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
                var flags = PendingIntentFlags.UpdateCurrent;
                if ((int)Build.VERSION.SdkInt >= 31)
                {
                    flags |= PendingIntentFlags.Immutable;
                }
                var contentIntent = PendingIntent.GetActivity(context, id.GetHashCode(), launchIntent, flags);
                builder.SetContentIntent(contentIntent);
            }

            // 弹出通知：id 用 GetHashCode 保证整数唯一性
            var notifMgr = NotificationManagerCompat.From(context);
            notifMgr?.Notify(id.GetHashCode(), builder.Build());

            Log.Info("ChildNotes", $"[NotiRecv] Shown id={id} title={title}");
        }
        catch (Exception ex)
        {
            Log.Error("ChildNotes", $"[NotiRecv] OnReceive failed: {ex}");
        }
    }
}
