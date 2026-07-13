using ChildNotes.Data.Repositories;
using ChildNotes.Infrastructure;
using ChildNotes.Models;

namespace ChildNotes.Services;

/// <summary>
/// 本地提醒调度服务：基于业务事件（记录喂奶/睡眠）调度/取消本地通知。
///
/// 设计原则：
/// - 不维护状态：每次调用都查询最新记录和配置，避免本地状态与 DB 不一致
/// - 失败静默：通知调度失败不影响记录写入主流程（与 SyncTrigger.NotifyWrite 一致）
/// - 幂等：同一 id 重复 Schedule 会覆盖旧的（AndroidLocalNotification.ScheduleAsync 已实现）
///
/// 业务场景：
/// 1. 喂奶记录后：调度间隔时长后的提醒；若间隔内再次喂奶，旧提醒取消、新提醒重新计时
/// 2. 睡眠开始记录后：调度超时后的"宝宝睡了好久了"提醒；若提前结束睡眠，取消提醒
///
/// 通知 ID 约定：
/// - feed-reminder：当前用户+当前宝宝的喂奶提醒（同一时刻只会有一个）
/// - sleep-reminder-{recordId}：单次睡眠记录的超时提醒（按记录 id 区分，避免连续睡眠记录互相覆盖）
///
/// 配置来源：从 reminder_config 表读取用户可调的阈值（默认 3 小时/4 小时），见 ReminderSettingsViewModel。
/// </summary>
public sealed class ReminderService
{
    private readonly RecordService _recordService;
    private readonly ReminderConfigRepository _configRepo;

    public ReminderService(RecordService recordService, ReminderConfigRepository configRepo)
    {
        _recordService = recordService;
        _configRepo = configRepo;
    }

    /// <summary>
    /// 喂奶记录后调度/重置喂奶提醒。
    /// 取消旧的 feed-reminder，基于本次喂奶时间重新调度阈值后的提醒。
    /// </summary>
    /// <param name="feedTime">本次喂奶时间（用于计算提醒触发时间）。</param>
    public void ScheduleFeedReminder(DateTime feedTime)
    {
        try
        {
            var localNoti = ServiceProvider.Instance.LocalNotification;
            if (!localNoti.IsSupported) return;

            var cfg = _configRepo.Get();
            if (!cfg.FeedReminderEnabled) return;

            const string id = "feed-reminder";
            // 提醒时间 = 喂奶时间 + 阈值；若已过期则立即触发（用户补记超阈值时长的旧记录）
            var fireAt = feedTime.AddHours(cfg.FeedIntervalHours);
            if (fireAt <= DateTime.Now)
            {
                fireAt = DateTime.Now.AddSeconds(5);
            }

            var babyName = ServiceProvider.Instance.AppState.CurrentBaby?.Name;
            var title = string.IsNullOrEmpty(babyName) ? "该喂奶了" : $"{babyName}该喂奶了";
            var body = $"距上次喂奶已 {cfg.FeedIntervalHours} 小时，记得记录下一次喂奶";

            // ScheduleAsync 内部会先取消同 id 的旧 PendingIntent 再重新调度，无需手动 Cancel
            _ = localNoti.ScheduleAsync(id, title, body, fireAt);
            DevLogger.Log("Reminder", $"ScheduleFeedReminder: fireAt={fireAt:O}");
        }
        catch (Exception ex)
        {
            DevLogger.Log("Reminder", $"ScheduleFeedReminder failed: {ex}");
        }
    }

    /// <summary>
    /// 取消喂奶提醒（用户手动取消或登出时调用）。
    /// </summary>
    public void CancelFeedReminder()
    {
        try
        {
            var localNoti = ServiceProvider.Instance.LocalNotification;
            if (!localNoti.IsSupported) return;
            _ = localNoti.CancelAsync("feed-reminder");
            DevLogger.Log("Reminder", "CancelFeedReminder");
        }
        catch (Exception ex)
        {
            DevLogger.Log("Reminder", $"CancelFeedReminder failed: {ex}");
        }
    }

    /// <summary>
    /// 睡眠开始后调度睡眠超时提醒。
    /// 阈值小时后若仍未结束睡眠，提醒用户"宝宝睡了好久了"。
    /// </summary>
    /// <param name="recordId">睡眠记录 ID（用于区分连续睡眠记录）。</param>
    /// <param name="sleepStartTime">睡眠开始时间。</param>
    public void ScheduleSleepReminder(string recordId, DateTime sleepStartTime)
    {
        try
        {
            var localNoti = ServiceProvider.Instance.LocalNotification;
            if (!localNoti.IsSupported) return;

            var cfg = _configRepo.Get();
            if (!cfg.SleepReminderEnabled) return;

            var id = $"sleep-reminder-{recordId}";
            var fireAt = sleepStartTime.AddHours(cfg.SleepTimeoutHours);
            if (fireAt <= DateTime.Now)
            {
                fireAt = DateTime.Now.AddSeconds(5);
            }

            var babyName = ServiceProvider.Instance.AppState.CurrentBaby?.Name;
            var title = string.IsNullOrEmpty(babyName) ? "宝宝睡了好久了" : $"{babyName}睡了好久了";
            var body = $"宝宝已睡 {cfg.SleepTimeoutHours} 小时，是否需要唤醒喂奶？";

            _ = localNoti.ScheduleAsync(id, title, body, fireAt);
            DevLogger.Log("Reminder", $"ScheduleSleepReminder: recordId={recordId} fireAt={fireAt:O}");
        }
        catch (Exception ex)
        {
            DevLogger.Log("Reminder", $"ScheduleSleepReminder failed: {ex}");
        }
    }

    /// <summary>
    /// 取消指定睡眠记录的超时提醒（睡眠结束时调用）。
    /// </summary>
    /// <param name="recordId">睡眠记录 ID。</param>
    public void CancelSleepReminder(string recordId)
    {
        try
        {
            var localNoti = ServiceProvider.Instance.LocalNotification;
            if (!localNoti.IsSupported) return;
            _ = localNoti.CancelAsync($"sleep-reminder-{recordId}");
            DevLogger.Log("Reminder", $"CancelSleepReminder: recordId={recordId}");
        }
        catch (Exception ex)
        {
            DevLogger.Log("Reminder", $"CancelSleepReminder failed: {ex}");
        }
    }
}
