using System;
using Android.App;
using Android.Content;
using AndroidX.Core.App;

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
    // 显式声明 IntentFilter：即使本 receiver 用显式 intent 启动，声明 action 有助于系统识别
    // 注：Android 14+ 对动态注册 receiver 有限制，静态声明更稳妥
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
                .SetSmallIcon(Resource.Drawable.Icon)  // 复用应用图标作为小图标
                .SetContentTitle(title)
                .SetContentText(body)
                .SetStyle(new NotificationCompat.BigTextStyle().BigText(body))  // 长文本可展开
                .SetAutoCancel(true)  // 点击后自动消失
                .SetPriority(NotificationCompat.PriorityDefault);  // 兼容 Android 7.1- 的优先级

            // 点击跳转：用 TaskStackBuilder 构建BackStack，未来接入 DeepLink 时可扩展
            // 当前简化：点击只打开 App（Launcher Intent）
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
            NotificationManagerCompat.From(context).Notify(id.GetHashCode(), builder.Build());

            Android.Util.Log.Info("ChildNotes", $"[NotiRecv] Shown id={id} title={title}");
        }
        catch (Exception ex)
        {
            Android.Util.Log.Error("ChildNotes", $"[NotiRecv] OnReceive failed: {ex}");
        }
    }
}
