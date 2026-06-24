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
    [ObservableProperty] private string _toastMessage = string.Empty;
    [ObservableProperty] private bool _showToast;

    public ObservableCollection<SignInTimelineItem> Timeline { get; } = new();
    public ObservableCollection<TaskDisplayItem> Tasks { get; } = new();

    public event Action? BackRequested;

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

    private async void ShowToastMessage(string msg)
    {
        ToastMessage = msg;
        ShowToast = true;
        await Task.Delay(2000);
        ShowToast = false;
    }

    public void Back() => BackRequested?.Invoke();
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
