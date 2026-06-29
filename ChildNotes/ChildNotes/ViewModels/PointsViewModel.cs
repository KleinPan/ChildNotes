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

    public void Load()
    {
        Refresh();
    }

    private void Refresh()
    {
        var dashboard = _pointsService.GetDashboard();
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

    [RelayCommand]
    private void SignIn()
    {
        if (Signing || TodaySigned) return;
        Signing = true;
        var dashboard = _pointsService.SignIn();
        Points = dashboard.Points;
        TotalEarned = dashboard.TotalEarned;
        TodaySigned = dashboard.TodaySigned;
        ContinuousDays = dashboard.ContinuousDays;
        SignButtonText = "今日已签到";

        Timeline.Clear();
        foreach (var item in dashboard.Timeline) Timeline.Add(item);

        ShowToastMessage($"签到成功 +{dashboard.TodayRewardPoints}分");
        Signing = false;
    }

    // 历史调用 ShowToastMessage，统一改走基类 DisplayToast（2000ms 时长由 ToastDurationMs 覆写控制）
    private void ShowToastMessage(string msg) => DisplayToast(msg);
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
