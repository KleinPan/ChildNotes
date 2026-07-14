using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Services;
using ChildNotes.Shared.Constants;
using ChildNotes.Shared.Dtos;

namespace ChildNotes.ViewModels.Home;

/// <summary>
/// 首页异常/生病追踪 ViewModel：管理异常状态（发烧/腹泻/其他）、恢复标记。
/// 从 HomeViewModel 拆分，职责单一化。
/// </summary>
public partial class AbnormalTrackingViewModel : ObservableObject
{
    private readonly RecordService _recordService = ServiceProvider.Instance.RecordService;
    private readonly LocaleManager _locale = LocaleManager.Instance;
    private DayStats? _lastStats;
    private List<ChildRecord> _lastAbnormalRecords = new();

    // ===== 异常/生病追踪状态（对齐小程序首页 fever/diarrhea/other-abnormal 三态） =====
    /// <summary>当前是否有活动异常（发烧/腹泻/其他异常任一）。</summary>
    [ObservableProperty] private bool _hasActiveAbnormal;
    /// <summary>是否存在「其他异常」（控制「已恢复」按钮可见性；发烧/腹泻通过各自入口恢复）。</summary>
    [ObservableProperty] private bool _hasOtherAbnormal;
    [ObservableProperty] private string _abnormalStatusText = string.Empty;
    [ObservableProperty] private string _abnormalSummaryText = string.Empty;

    public AbnormalTrackingViewModel()
    {
        _locale.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(AppLanguage lang)
    {
        // 语言切换后用上次快照重新计算文案
        if (_lastStats is not null || _lastAbnormalRecords.Count > 0)
            ApplyAbnormal(_lastStats, _lastAbnormalRecords);
    }

    /// <summary>
    /// 从快照数据应用异常/生病追踪状态（不再重复查询 DB）。
    /// 依据今日 DayStats 的三态标志（发烧/腹泻/其他异常），
    /// 并从异常记录中提取最新摘要。对齐小程序首页 getTodayStats 驱动的状态展示。
    /// </summary>
    public void ApplyAbnormal(DayStats? stats, List<ChildRecord> abnormalRecords)
    {
        _lastStats = stats;
        _lastAbnormalRecords = abnormalRecords;

        if (stats is null || (!stats.HasFever && !stats.HasDiarrhea && !stats.HasOtherAbnormal))
        {
            ResetAbnormal();
            return;
        }

        HasActiveAbnormal = true;
        HasOtherAbnormal = stats.HasOtherAbnormal;

        var statusParts = new List<string>();
        if (stats.HasFever) statusParts.Add(_locale.GetString("Home_Abnormal_Fever", "发烧"));
        if (stats.HasDiarrhea) statusParts.Add(_locale.GetString("Home_Abnormal_Diarrhea", "腹泻"));
        if (stats.HasOtherAbnormal) statusParts.Add(_locale.GetString("Home_Abnormal_Other", "其他异常"));
        AbnormalStatusText = string.Join(" · ", statusParts);

        // 摘要：从已查快照中取最新一条异常记录，拼体温 + 备注/其他描述
        var latestAbnormal = abnormalRecords.FirstOrDefault();
        if (latestAbnormal is not null)
        {
            var summaryParts = new List<string>();
            if (latestAbnormal.TemperatureValue.HasValue)
                summaryParts.Add($"{latestAbnormal.TemperatureValue:F1}℃");
            AbnormalRecordDto? dto = null;
            try { dto = latestAbnormal.GetPayload<AbnormalRecordDto>(); } catch { }
            if (dto is not null)
            {
                if (dto.Respiratory.Count > 0)
                    summaryParts.Add(string.Format(_locale.GetString("Home_Abnormal_Respiratory", "呼吸道：{0}"), string.Join("、", dto.Respiratory)));
                if (dto.Vomit) summaryParts.Add(_locale.GetString("Home_Abnormal_Vomit", "呕吐"));
                if (dto.Medicine) summaryParts.Add(_locale.GetString("Home_Abnormal_Medicated", "已用药"));
                if (!string.IsNullOrWhiteSpace(dto.Note)) summaryParts.Add(dto.Note);
            }
            var time = ServiceProvider.Instance.DateTimeFormatter.FormatTime(latestAbnormal.RecordTime);
            summaryParts.Insert(0, time);
            AbnormalSummaryText = string.Join(" · ", summaryParts);
        }
        else
        {
            AbnormalSummaryText = _locale.GetString("Home_Abnormal_Summary", "今日有异常记录，请关注宝宝状态");
        }
    }

    private void ResetAbnormal()
    {
        HasActiveAbnormal = false;
        HasOtherAbnormal = false;
        AbnormalStatusText = string.Empty;
        AbnormalSummaryText = string.Empty;
    }

    /// <summary>
    /// 标记「其他异常」已恢复：写入一条 abnormal_resolved 占位记录，
    /// 对齐小程序 markAbnormalResolved 的语义（写入恢复标记记录，聚合时不再计入活动异常）。
    /// </summary>
    [RelayCommand]
    private void MarkAbnormalResolved()
    {
        _recordService.MarkResolved(RecordType.AbnormalResolved);
        // 通知宿主刷新（通过事件，避免直接依赖 HomeViewModel）
        RefreshRequested?.Invoke();
    }

    /// <summary>请求宿主刷新数据（标记恢复后需刷新首页）。</summary>
    public event Action? RefreshRequested;
}
