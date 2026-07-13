using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Data.Repositories;
using ChildNotes.Infrastructure;
using ChildNotes.Models;

namespace ChildNotes.ViewModels;

/// <summary>
/// 提醒设置页 ViewModel：管理 reminder_config（喂奶/睡眠提醒阈值与开关）。
///
/// 保存模式：
/// - 开关（FeedReminderEnabled/SleepReminderEnabled）：切换即持久化（与 SyncSettingsViewModel 一致）
/// - 时长（FeedIntervalHours/SleepTimeoutHours）：Slider 调节后点"保存"按钮显式持久化（避免拖动过程频繁写库）
///
/// 配置生效：ReminderService.ScheduleFeedReminder/ScheduleSleepReminder 每次读取最新配置，
/// 因此修改后立即对后续调度的提醒生效；已调度的旧提醒不会自动更新（需用户重新记录一次喂奶/睡眠触发）。
/// </summary>
public partial class ReminderSettingsViewModel : ViewModelBase, IActivatable
{
    private readonly ReminderConfigRepository _cfgRepo = ServiceProvider.Instance.ReminderConfigRepository;

    [ObservableProperty] private bool _feedReminderEnabled;
    [ObservableProperty] private int _feedIntervalHours = 3;
    [ObservableProperty] private bool _sleepReminderEnabled;
    [ObservableProperty] private int _sleepTimeoutHours = 4;

    public ReminderSettingsViewModel()
    {
        Title = "提醒设置";
    }

    public void Activate() => Load();

    private void Load()
    {
        var cfg = _cfgRepo.Get();
        FeedReminderEnabled = cfg.FeedReminderEnabled;
        FeedIntervalHours = cfg.FeedIntervalHours;
        SleepReminderEnabled = cfg.SleepReminderEnabled;
        SleepTimeoutHours = cfg.SleepTimeoutHours;
    }

    /// <summary>喂奶提醒开关切换时自动持久化（无需点保存按钮）。</summary>
    partial void OnFeedReminderEnabledChanged(bool value) => PersistEnabled();

    /// <summary>睡眠提醒开关切换时自动持久化。</summary>
    partial void OnSleepReminderEnabledChanged(bool value) => PersistEnabled();

    private void PersistEnabled()
    {
        // 只读回填时 Load() 会触发 OnXxxChanged，此处用当前 UI 值整体保存
        var cfg = _cfgRepo.Get();
        cfg.FeedReminderEnabled = FeedReminderEnabled;
        cfg.SleepReminderEnabled = SleepReminderEnabled;
        _cfgRepo.Save(cfg);
    }

    [RelayCommand]
    private void Save()
    {
        var cfg = _cfgRepo.Get();
        cfg.FeedIntervalHours = FeedIntervalHours;
        cfg.SleepTimeoutHours = SleepTimeoutHours;
        cfg.FeedReminderEnabled = FeedReminderEnabled;
        cfg.SleepReminderEnabled = SleepReminderEnabled;
        _cfgRepo.Save(cfg);
        DisplayToast("配置已保存");
    }
}
