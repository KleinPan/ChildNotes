using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

public partial class PointsViewModel : ViewModelBase
{
    private readonly PointsService _pointsService = ServiceProvider.Instance.PointsService;

    [ObservableProperty] private int _points;
    [ObservableProperty] private int _totalEarned;
    [ObservableProperty] private int _totalSpent;
    [ObservableProperty] private bool _todaySigned;
    [ObservableProperty] private int _continuousDays;
    [ObservableProperty] private bool _signing;
    [ObservableProperty] private string _signButtonText = "立即签到";

    public ObservableCollection<SignInTimelineItem> Timeline { get; } = new();
    public ObservableCollection<TaskDisplayItem> Tasks { get; } = new();

    /// <summary>沿用历史 2000ms 显示时长。</summary>
    protected override int ToastDurationMs => 2000;

    /// <summary>
    /// 异步加载：DB 查询放到后台线程，UI 线程仅做属性赋值。
    /// 用于弹层"先打开再加载"模式，避免阻塞 UI。
    /// 修复：原还有同步 Load() / Refresh() 重复实现，已删除（死代码）。
    /// </summary>
    public async Task LoadAsync()
    {
        var dashboard = await Task.Run(() => _pointsService.GetDashboard());
        ApplyDashboard(dashboard);
    }

    private void ApplyDashboard(PointsDashboard dashboard)
    {
        Points = dashboard.Points;
        TotalEarned = dashboard.TotalEarned;
        TotalSpent = dashboard.TotalSpent;
        TodaySigned = dashboard.TodaySigned;
        ContinuousDays = dashboard.ContinuousDays;
        SignButtonText = TodaySigned ? "今日已签到" : "立即签到";

        Timeline.Clear();
        foreach (var item in dashboard.Timeline) Timeline.Add(item);

        Tasks.Clear();
        foreach (var t in dashboard.Tasks) Tasks.Add(new TaskDisplayItem(t));
    }

    /// <summary>修复：原 SignIn 同步调 _pointsService.SignIn 阻塞 UI，改为后台线程执行。</summary>
    [RelayCommand]
    private async Task SignIn()
    {
        if (Signing || TodaySigned) return;
        Signing = true;
        var dashboard = await Task.Run(() => _pointsService.SignIn());
        Points = dashboard.Points;
        TotalEarned = dashboard.TotalEarned;
        TodaySigned = dashboard.TodaySigned;
        ContinuousDays = dashboard.ContinuousDays;
        SignButtonText = "今日已签到";

        Timeline.Clear();
        foreach (var item in dashboard.Timeline) Timeline.Add(item);

        DisplayToast($"签到成功 +{dashboard.TodayRewardPoints}分");
        Signing = false;
    }
}

public sealed class TaskDisplayItem
{
    public string Name { get; }
    public string Desc { get; }
    public string RewardText { get; }
    public TaskDisplayItem(TaskItem task)
    {
        Name = task.Name;
        Desc = task.Desc;
        RewardText = $"+{task.Reward}分";
    }
}
