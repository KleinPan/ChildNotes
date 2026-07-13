namespace ChildNotes.Models;

/// <summary>
/// 提醒配置（单行配置表，id=1）。
/// 用户可在"提醒设置"页调整阈值，ReminderService 读取此配置调度本地通知。
/// </summary>
public sealed class ReminderConfig
{
    public int Id { get; set; } = 1;

    /// <summary>喂奶提醒总开关。</summary>
    public bool FeedReminderEnabled { get; set; } = true;

    /// <summary>喂奶间隔提醒阈值（小时）。默认 3 小时。</summary>
    public int FeedIntervalHours { get; set; } = 3;

    /// <summary>睡眠超时提醒总开关。</summary>
    public bool SleepReminderEnabled { get; set; } = true;

    /// <summary>睡眠超时提醒阈值（小时）。默认 4 小时。</summary>
    public int SleepTimeoutHours { get; set; } = 4;
}
