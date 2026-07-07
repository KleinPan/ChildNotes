using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Models.Home;
using ChildNotes.Shared.Constants;
using ChildNotes.Shared.Dtos;
using ChildNotes.Services;

namespace ChildNotes.ViewModels.Home;

/// <summary>
/// 首页活动追踪 ViewModel：管理最近活动、时间轴分组、分页加载、定时刷新。
/// 从 HomeViewModel 拆分，职责单一化。
/// </summary>
public partial class ActivityTrackingViewModel : ObservableObject
{
    private readonly RecordService _recordService = ServiceProvider.Instance.RecordService;

    [ObservableProperty] private bool _isActivityExpanded;
    [ObservableProperty] private bool _isActivityDetailOpen;
    [ObservableProperty] private ActivityLatestItem? _lastActivity;
    [ObservableProperty] private string _activityTimeSince = "--";
    [ObservableProperty] private ObservableCollection<ActivityTimelineGroup> _activityTimelineGroups = new();
    /// <summary>活动详情面板加载状态：true 时显示 loading 占位，避免 UI 卡顿无反馈。</summary>
    [ObservableProperty] private bool _isActivityLoading;

    // ===== 删除确认对话框状态（对齐喂养页 FeedingViewModel.ShowDeleteConfirm 模式） =====
    [ObservableProperty] private bool _showActivityDeleteConfirm;
    [ObservableProperty] private string _deleteActivityTitle = string.Empty;
    private string _deletingActivityId = string.Empty;

    /// <summary>请求宿主刷新首页（删除活动记录后需刷新最近活动展示）。</summary>
    public event Action? RefreshRequested;

    // 距上次活动时间实时刷新（对齐小程序 startTimer 30 秒间隔）
    private readonly DispatcherTimer _activitySinceTimer;
    private DateTime? _lastActivityTime;

    /// <summary>
    /// 活动时间轴每页加载条数：首屏 20 条，滚动加载每次 +20。
    /// 原 100 条全量加载导致面板打开卡顿 200-500ms，分页后首屏 &lt; 50ms。
    /// </summary>
    private const int ActivityPageSize = 20;
    private int _activityLoadedCount;
    /// <summary>活动记录全量缓存：BuildActivityTimelineAsync 查询一次后，LoadMoreActivities 直接从缓存切片，避免重复 DB 查询。</summary>
    private List<ChildRecord>? _activityCache;

    public ActivityTrackingViewModel()
    {
        _activitySinceTimer = new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Normal, OnActivitySinceTick);
    }

    /// <summary>
    /// 从快照数据应用活动追踪模块（不再重复查询 DB）。
    /// 最近一条活动 = activities 列表的第一条（列表已按 record_time DESC 返回）。
    /// </summary>
    public void ApplyActivity(List<ChildRecord> activities)
    {
        // 刷新即重置展开/详情状态（对齐小程序 pageVersion observer）
        IsActivityExpanded = false;
        IsActivityDetailOpen = false;
        ActivityTimelineGroups.Clear();

        if (activities.Count == 0)
        {
            ResetActivity();
            return;
        }

        var latest = activities[0];
        var dto = latest.GetPayload<ActivityRecordDto>();
        _lastActivityTime = latest.RecordTime;
        LastActivity = new ActivityLatestItem(
            dto?.Name ?? string.Empty,
            dto?.Category ?? latest.RecordSubType ?? "play",
            dto?.Duration,
            ServiceProvider.Instance.DateTimeFormatter.FormatDateTime(latest.RecordTime));

        UpdateActivityTimeSince();
        StartActivitySinceTimer();
    }

    /// <summary>失效活动时间轴缓存（保存记录后 RefreshAsync 会触发，下次打开活动面板需重查 DB 取最新数据）。</summary>
    public void InvalidateCache()
    {
        _activityCache = null;
    }

    private void ResetActivity()
    {
        StopActivitySinceTimer();
        _lastActivityTime = null;
        LastActivity = null;
        ActivityTimeSince = "--";
        IsActivityExpanded = false;
        IsActivityDetailOpen = false;
        ActivityTimelineGroups.Clear();
    }

    /// <summary>计算"X小时Y分钟前 / Y分钟前"，对齐小程序 timeSince 格式。</summary>
    private void UpdateActivityTimeSince()
    {
        if (!_lastActivityTime.HasValue)
        {
            ActivityTimeSince = "--";
            return;
        }
        var diff = DateTime.Now - _lastActivityTime.Value;
        var totalMin = Math.Max(0, (int)diff.TotalMinutes);
        var h = totalMin / 60;
        var m = totalMin % 60;
        ActivityTimeSince = h > 0 ? $"{h}小时{m}分钟前" : $"{m}分钟前";
    }

    private void StartActivitySinceTimer()
    {
        // 构造时已订阅 Tick 回调，这里只需 Stop+Start 重置计时周期
        _activitySinceTimer.Stop();
        _activitySinceTimer.Start();
    }

    private void StopActivitySinceTimer()
    {
        _activitySinceTimer.Stop();
    }

    private void OnActivitySinceTick(object? sender, EventArgs e)
    {
        UpdateActivityTimeSince();
    }

    /// <summary>
    /// 异步构建活动时间轴分组（对齐小程序 buildTimeline）。
    /// DB 查询放到后台线程，UI 线程仅做分组填充；首屏只加载 20 条，剩余按需加载。
    /// 记录列表已按时间倒序（最新在前），分组时保持该顺序：今天在最上。
    /// </summary>
    private async Task BuildActivityTimelineAsync()
    {
        IsActivityLoading = true;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // 后台线程查询 DB（避免阻塞 UI 线程），结果缓存供 LoadMoreActivities 复用
            _activityCache = await Task.Run(() => _recordService.GetByType(RecordType.Activity, 100));
            DevLogger.Log("ActivityPerf", $"BuildActivityTimelineAsync: DB query {_activityCache.Count} items in {sw.ElapsedMilliseconds}ms");

            // UI 线程：分页填充首屏
            ActivityTimelineGroups.Clear();
            _activityLoadedCount = 0;
            AppendActivityPage(_activityCache, ActivityPageSize);
            OnPropertyChanged(nameof(HasMoreActivities));
            DevLogger.Log("ActivityPerf", $"BuildActivityTimelineAsync: first page rendered, total={sw.ElapsedMilliseconds}ms, items={_activityLoadedCount}");
        }
        finally
        {
            IsActivityLoading = false;
        }
    }

    /// <summary>追加一页活动记录到现有分组（保持日期分组逻辑）。</summary>
    private void AppendActivityPage(List<ChildRecord> activities, int pageSize)
    {
        var todayStr = DateTime.Today.ToString("yyyy-MM-dd");
        ActivityTimelineGroup? current = null;
        var lastDate = string.Empty;
        // 复用最后一个已有分组（如果日期相同）
        if (ActivityTimelineGroups.Count > 0)
        {
            current = ActivityTimelineGroups[^1];
            lastDate = current.IsToday ? todayStr : current.Label;
        }

        var endIndex = Math.Min(_activityLoadedCount + pageSize, activities.Count);
        for (var i = _activityLoadedCount; i < endIndex; i++)
        {
            var rec = activities[i];
            var dateStr = rec.RecordTime.ToString("yyyy-MM-dd");
            if (dateStr != lastDate)
            {
                lastDate = dateStr;
                current = new ActivityTimelineGroup(
                    dateStr == todayStr ? "今天" : dateStr,
                    dateStr == todayStr);
                ActivityTimelineGroups.Add(current);
            }
            var dto = rec.GetPayload<ActivityRecordDto>();
            current!.Items.Add(new ActivityTimelineItem(
                rec.Id,
                dto?.Name ?? string.Empty,
                dto?.Category ?? rec.RecordSubType ?? "play",
                dto?.Duration,
                ServiceProvider.Instance.DateTimeFormatter.FormatDateTime(rec.RecordTime)));
        }
        _activityLoadedCount = endIndex;
    }

    /// <summary>
    /// 是否还有更多活动记录可加载（用于"加载更多"按钮显隐）。
    /// 基于实际缓存数量判断：已加载数 &lt; 缓存总数时才显示按钮。
    /// 无记录或已全部加载完成时返回 false。
    /// </summary>
    public bool HasMoreActivities => _activityCache is not null && _activityLoadedCount < _activityCache.Count;

    [RelayCommand(CanExecute = nameof(CanLoadMoreActivities))]
    private async Task LoadMoreActivities()
    {
        if (IsActivityLoading) return;
        // 缓存未命中（理论上不会发生，BuildActivityTimelineAsync 总会先调用）：兜底查一次
        if (_activityCache is null)
        {
            IsActivityLoading = true;
            try
            {
                _activityCache = await Task.Run(() => _recordService.GetByType(RecordType.Activity, 100));
            }
            finally
            {
                IsActivityLoading = false;
            }
        }
        IsActivityLoading = true;
        try
        {
            // 直接从缓存切片，避免每次"加载更多"都重查 DB
            AppendActivityPage(_activityCache, ActivityPageSize);
            OnPropertyChanged(nameof(HasMoreActivities));
            LoadMoreActivitiesCommand.NotifyCanExecuteChanged();
        }
        finally
        {
            IsActivityLoading = false;
        }
    }

    private bool CanLoadMoreActivities => HasMoreActivities && !IsActivityLoading;

    [RelayCommand]
    private void ToggleActivityPanel()
    {
        IsActivityExpanded = !IsActivityExpanded;
    }

    [RelayCommand]
    private async Task ToggleActivityDetail()
    {
        // 首次打开时异步构建时间轴分组（对齐小程序 onToggleDetail）
        if (!IsActivityDetailOpen)
        {
            // 立即打开面板（显示 loading 占位），后台异步加载数据
            IsActivityDetailOpen = true;
            await BuildActivityTimelineAsync();
        }
        else
        {
            IsActivityDetailOpen = false;
        }
    }

    [RelayCommand]
    private void CloseActivityDetail()
    {
        IsActivityDetailOpen = false;
    }

    /// <summary>点击时间轴卡片上的删除按钮：记录待删除项并弹出确认对话框。</summary>
    public void RequestDeleteActivity(ActivityTimelineItem item)
    {
        _deletingActivityId = item.RecordId;
        DeleteActivityTitle = $"{item.CategoryEmoji} {item.Name} {item.Time}";
        ShowActivityDeleteConfirm = true;
    }

    [RelayCommand]
    private void CancelDeleteActivity()
    {
        ShowActivityDeleteConfirm = false;
    }

    [RelayCommand]
    private void ConfirmDeleteActivity()
    {
        if (string.IsNullOrEmpty(_deletingActivityId)) return;
        _recordService.Delete(_deletingActivityId);
        ShowActivityDeleteConfirm = false;

        // 删除后就地刷新时间轴缓存（避免用户重新打开面板才能看到效果）
        _activityCache = null;
        _ = BuildActivityTimelineAsync();

        // 通知宿主刷新首页最近活动展示
        RefreshRequested?.Invoke();
    }
}
