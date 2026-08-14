using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

public partial class PointsViewModel : ViewModelBase
{
    private readonly PointsService _pointsService = ServiceProvider.Instance.PointsService;
    private readonly PointsApiClient _pointsApi = ServiceProvider.Instance.PointsApiClient;
    private readonly LocaleManager _locale = LocaleManager.Instance;

    [ObservableProperty] private int _points;
    [ObservableProperty] private int _totalEarned;
    [ObservableProperty] private int _totalSpent;
    [ObservableProperty] private bool _todaySigned;
    [ObservableProperty] private int _continuousDays;
    [ObservableProperty] private bool _signing;
    [ObservableProperty] private string _signButtonText = "立即签到";
    /// <summary>任务列表是否从后端加载（server 模式）。false 表示回退到本地展示，领取按钮不可用。</summary>
    [ObservableProperty] private bool _tasksLoadedFromServer;
    /// <summary>累计获得/已使用积分汇总文案（"累计获得 {0} 分 · 已使用 {1} 分"），供 XAML 直接绑定。</summary>
    [ObservableProperty] private string _totalEarnSpentText = string.Empty;

    public ObservableCollection<SignInTimelineItem> Timeline { get; } = new();
    public ObservableCollection<TaskDisplayItem> Tasks { get; } = new();

    /// <summary>沿用历史 2000ms 显示时长。</summary>
    protected override int ToastDurationMs => 2000;

    public PointsViewModel()
    {
        _locale.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(AppLanguage lang)
    {
        // 格式串依赖语言，切换时同步刷新
        RefreshTotalEarnSpentText();
    }

    /// <summary>刷新累计获得/已使用汇总文案。</summary>
    private void RefreshTotalEarnSpentText()
    {
        TotalEarnSpentText = string.Format(
            _locale.GetString("Points_TotalEarnSpent", "累计获得 {0} 分 · 已使用 {1} 分"),
            TotalEarned, TotalSpent);
    }

    /// <summary>
    /// 异步加载：DB 查询放到后台线程，UI 线程仅做属性赋值。
    /// 用于弹层"先打开再加载"模式，避免阻塞 UI。
    /// </summary>
    public async Task LoadAsync()
    {
        var dashboard = await Task.Run(() => _pointsService.GetDashboard());
        ApplyDashboard(dashboard);

        // 加载任务列表前先触发一次同步，确保本地最新记录（如刚记录的喂奶/尿布）已推送到后端。
        // 后端任务完成判定查的是 PostgreSQL 的 ChildRecords，若未同步则任务状态会显示"未完成"。
        // 同步是异步的，不阻塞 UI；同步失败或超时也不影响后续任务列表加载（降级为后端当前状态）。
        try
        {
            await Task.Run(() => ServiceProvider.Instance.SyncTrigger.RunNowAsync());
        }
        catch { /* 同步失败不阻塞任务列表加载 */ }

        // 任务列表优先从后端加载（含完成/领取状态，支持领取）；
        // 后端不可达时回退到本地 dashboard.Tasks（仅展示，不可领取）。
        // HTTP 调用包到 Task.Run：Android 首次 HttpClient 调用的 DNS/SSL 握手
        // 可能在调用线程同步执行，与 SignIn/ClaimTask 保持一致。
        var serverTasks = await Task.Run(() => _pointsApi.GetTasksAsync());
        if (serverTasks is { Count: > 0 })
        {
            Tasks.Clear();
            foreach (var t in serverTasks) Tasks.Add(new TaskDisplayItem(t, claim: ClaimTaskCommand));
            TasksLoadedFromServer = true;
        }
        else
        {
            Tasks.Clear();
            // 本地回退：不可领取（无 ClaimCommand），仅展示任务名和奖励
            foreach (var t in dashboard.Tasks) Tasks.Add(new TaskDisplayItem(t));
            TasksLoadedFromServer = false;
        }
    }

    private void ApplyDashboard(PointsDashboard dashboard)
    {
        Points = dashboard.Points;
        TotalEarned = dashboard.TotalEarned;
        TotalSpent = dashboard.TotalSpent;
        TodaySigned = dashboard.TodaySigned;
        ContinuousDays = dashboard.ContinuousDays;
        SignButtonText = TodaySigned ? "今日已签到" : "立即签到";
        RefreshTotalEarnSpentText();

        Timeline.Clear();
        foreach (var item in dashboard.Timeline) Timeline.Add(item);
    }

    /// <summary>
    /// 签到：先本地 SQLite 签到（立即反馈），再后台同步到后端（fire-and-forget）。
    /// 后端同步失败不影响用户体验，下次打开 App 会重新同步。
    /// </summary>
    [RelayCommand]
    private async Task SignIn()
    {
        if (Signing || TodaySigned) return;
        Signing = true;

        // 先本地签到：立即更新 UI，让用户无感知延迟。
        // 本地 SQLite 签到是毫秒级，用户点击后立即看到"今日已签到"和积分变化。
        var localDashboard = await Task.Run(() => _pointsService.SignIn());
        Points = localDashboard.Points;
        TotalEarned = localDashboard.TotalEarned;
        TodaySigned = localDashboard.TodaySigned;
        ContinuousDays = localDashboard.ContinuousDays;
        SignButtonText = "今日已签到";

        Timeline.Clear();
        foreach (var item in localDashboard.Timeline) Timeline.Add(item);

        DisplayToast($"签到成功 +{localDashboard.TodayRewardPoints}分");
        Signing = false;

        // 后台同步到后端（fire-and-forget）：确保后端 PostgreSQL 积分与本地一致，
        // 避免 AI 分析时后端积分不足误报。HTTP 调用包到 Task.Run 避免 UI 线程阻塞。
        // 后端签到失败不影响用户体验（本地已签到，下次打开会重新同步）。
        _ = Task.Run(async () =>
        {
            try
            {
                var serverResult = await _pointsApi.SignInAsync();
                if (serverResult is not null && !serverResult.AlreadySignedIn)
                {
                    // 后端签到成功：用后端返回的积分覆盖本地 SQLite
                    _pointsService.SyncPointsFromServer(serverResult.Points, serverResult.TotalEarned, serverResult.TotalSpent);
                    DevLogger.Log("Points", $"后端签到同步成功: points={serverResult.Points}");
                }
            }
            catch (Exception ex)
            {
                DevLogger.Log("Points", "后端签到同步失败: " + ex.Message);
            }
        });
    }

    /// <summary>
    /// 领取日常任务奖励。调用后端 POST /api/points/tasks/{key}/claim。
    /// 成功后更新该任务的 IsClaimed 和积分余额；失败显示后端返回的错误消息。
    /// </summary>
    [RelayCommand]
    private async Task ClaimTask(TaskDisplayItem? item)
    {
        if (item is null || !item.CanClaim || item.Claiming) return;
        item.Claiming = true;
        try
        {
            var result = await Task.Run(() => _pointsApi.ClaimTaskAsync(item.TaskKey));
            item.IsClaimed = true;
            item.RefreshCanClaim();
            Points = (int)result.Points;
            TotalEarned = (int)result.TotalEarned;
            RefreshTotalEarnSpentText();
            DisplayToast($"领取成功 +{result.AwardedPoints}分");
        }
        catch (PointsApiException ex)
        {
            // 任务未完成/已领取等业务错误：显示后端 msg
            DisplayToast(ex.Message);
            // 已领取错误：同步刷新本地状态
            if (ex.IsTaskAlreadyClaimed)
            {
                item.IsClaimed = true;
                item.RefreshCanClaim();
            }
        }
        catch (Exception ex)
        {
            DisplayToast($"领取失败：{ex.Message}");
        }
        finally
        {
            item.Claiming = false;
        }
    }
}

/// <summary>
/// 任务展示项。从后端 ServerTaskItem 映射时支持领取；从本地 TaskItem 映射时仅展示。
/// </summary>
public sealed class TaskDisplayItem : ObservableObject
{
    public string TaskKey { get; }
    public string Name { get; }
    public string Desc { get; }
    public string RewardText { get; }
    /// <summary>"share"（邀请任务）或 "claim"（日常任务）。</summary>
    public string Action { get; }

    private bool _isCompleted;
    public bool IsCompleted
    {
        get => _isCompleted;
        set
        {
            if (_isCompleted == value) return;
            _isCompleted = value;
            OnPropertyChanged();
            RefreshCanClaim();
        }
    }

    private bool _isClaimed;
    public bool IsClaimed
    {
        get => _isClaimed;
        set
        {
            if (_isClaimed == value) return;
            _isClaimed = value;
            OnPropertyChanged();
            RefreshCanClaim();
        }
    }

    private bool _canClaim;
    /// <summary>是否可领取：已完成且未领取且绑定了领取命令。</summary>
    public bool CanClaim
    {
        get => _canClaim;
        private set => SetProperty(ref _canClaim, value);
    }

    private bool _showRewardLabel;
    /// <summary>是否显示奖励数值标签（未完成且未领取时显示）。与"已领取"标签和"领取"按钮互斥。</summary>
    public bool ShowRewardLabel
    {
        get => _showRewardLabel;
        private set => SetProperty(ref _showRewardLabel, value);
    }

    private bool _claiming;
    /// <summary>是否正在领取中（用于禁用按钮，防止重复点击）。</summary>
    public bool Claiming
    {
        get => _claiming;
        set => SetProperty(ref _claiming, value);
    }

    /// <summary>领取命令（从后端加载时注入，本地回退时为 null）。</summary>
    public IAsyncRelayCommand? ClaimCommand { get; }

    /// <summary>后端任务项构造：支持领取。</summary>
    public TaskDisplayItem(ServerTaskItem task, IAsyncRelayCommand? claim = null)
    {
        TaskKey = task.TaskKey;
        Name = task.Title;
        Desc = task.Description;
        RewardText = $"+{task.Points}分";
        Action = task.Action;
        _isCompleted = task.IsCompleted;
        _isClaimed = task.IsClaimed;
        ClaimCommand = claim;
        _canClaim = _isCompleted && !_isClaimed && claim is not null;
        _showRewardLabel = !_isClaimed && !_canClaim;
    }

    /// <summary>本地任务项构造：仅展示，不可领取（无 ClaimCommand）。</summary>
    public TaskDisplayItem(TaskItem task)
    {
        TaskKey = task.Code;
        Name = task.Name;
        Desc = task.Desc;
        RewardText = $"+{task.Reward}分";
        Action = "local";
        _isCompleted = task.IsCompleted;
        _isClaimed = false;
        ClaimCommand = null;
        _canClaim = false;
        _showRewardLabel = true;
    }

    /// <summary>重新计算 CanClaim 和 ShowRewardLabel（在 IsCompleted/IsClaimed 变更后调用）。</summary>
    public void RefreshCanClaim()
    {
        CanClaim = _isCompleted && !_isClaimed && ClaimCommand is not null;
        ShowRewardLabel = !_isClaimed && !CanClaim;
    }
}
