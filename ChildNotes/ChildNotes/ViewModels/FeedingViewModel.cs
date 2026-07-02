using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Shared.Dtos;
using ChildNotes.Services;
using ChildNotes.Shared.Constants;

namespace ChildNotes.ViewModels;

public partial class FeedingViewModel : ViewModelBase, IActivatable
{
    private readonly RecordService _recordService = ServiceProvider.Instance.RecordService;
    private readonly StatisticsService _statsService = ServiceProvider.Instance.StatisticsService;

    [ObservableProperty] private DateTime _selectedDate = DateTime.Today;
    [ObservableProperty] private string _displayDate = string.Empty;
    [ObservableProperty] private DayStats? _dayStats;
    [ObservableProperty] private bool _isToday = true;
    [ObservableProperty] private bool _isNextDayEnabled;
    [ObservableProperty] private bool _showDeleteConfirm;
    [ObservableProperty] private string _deleteItemTitle = string.Empty;

    public ObservableCollection<RecordDisplayItem> Records { get; } = new();

    /// <summary>请求主壳层打开编辑记录抽屉（复用 RecordSheet 编辑模式）。</summary>
    public event Action<ChildRecord>? EditRequested;

    private string _deletingRecordId = string.Empty;

    public FeedingViewModel()
    {
    }

    /// <summary>沿用历史 2000ms 显示时长。</summary>
    protected override int ToastDurationMs => 2000;

    public void Activate()
    {
        _ = LoadDataAsync();
    }

    /// <summary>
    /// 异步加载数据：DB 查询移到后台线程，避免切换 tab 时阻塞 UI 100-300ms。
    /// 修复：原 LoadData 在 UI 线程同步调 GetDayStats + GetByDate。
    /// </summary>
    private async Task LoadDataAsync()
    {
        var date = SelectedDate;
        // 后台线程执行 DB 查询，UI 线程并行准备日期显示
        var (stats, records) = await Task.Run(() =>
            (_statsService.GetDayStats(date), _recordService.GetByDate(date)));

        DisplayDate = ServiceProvider.Instance.DateTimeFormatter.FormatChineseMonthDay(date) + " " + GetWeekday(date);
        IsToday = date.Date == DateTime.Today;
        IsNextDayEnabled = date.Date < DateTime.Today;
        DayStats = stats;

        Records.Clear();
        // 疫苗记录仅在首页"疫苗追踪"模块展示，活动记录仅在首页"活动追踪"模块展示，
        // 两者均不在喂养页面记录列表中显示（对齐小程序 buildRecordList 只处理 feeds/diapers/sleeps 等的逻辑）
        foreach (var r in records.OrderBy(x => x.RecordTime)
                     .Where(x => x.RecordType != RecordType.Vaccine && x.RecordType != RecordType.Activity))
        {
            Records.Add(new RecordDisplayItem(r));
        }
    }

    private static string GetWeekday(DateTime d) => d.DayOfWeek switch
    {
        DayOfWeek.Monday => "周一",
        DayOfWeek.Tuesday => "周二",
        DayOfWeek.Wednesday => "周三",
        DayOfWeek.Thursday => "周四",
        DayOfWeek.Friday => "周五",
        DayOfWeek.Saturday => "周六",
        _ => "周日",
    };

    [RelayCommand]
    private void PrevDay()
    {
        SelectedDate = SelectedDate.AddDays(-1);
        _ = LoadDataAsync();
    }

    [RelayCommand]
    private void NextDay()
    {
        if (SelectedDate < DateTime.Today)
        {
            SelectedDate = SelectedDate.AddDays(1);
            _ = LoadDataAsync();
        }
    }

    [RelayCommand]
    private void GoToday()
    {
        SelectedDate = DateTime.Today;
        _ = LoadDataAsync();
    }

    public void RequestDelete(RecordDisplayItem item)
    {
        _deletingRecordId = item.Record.Id;
        DeleteItemTitle = $"{item.Icon} {item.TitleText} {item.TimeText}";
        ShowDeleteConfirm = true;
    }

    public void ToggleSwipe(RecordDisplayItem item)
    {
        foreach (var r in Records)
        {
            r.IsSwipeOpen = r == item && !r.IsSwipeOpen;
        }
    }

    public void EditRecord(RecordDisplayItem item)
    {
        foreach (var r in Records) r.IsSwipeOpen = false;
        // 委托给 MainShellViewModel.OpenEditRecord，复用 RecordSheet 编辑模式
        EditRequested?.Invoke(item.Record);
    }

    [RelayCommand]
    private void CancelDelete()
    {
        ShowDeleteConfirm = false;
    }

    [RelayCommand]
    private void ConfirmDelete()
    {
        _recordService.Delete(_deletingRecordId);
        ShowDeleteConfirm = false;
        _ = LoadDataAsync();
        DisplayToast("已删除记录");
    }
}

public sealed partial class RecordDisplayItem : ObservableObject
{
    public ChildRecord Record { get; }
    public string TimeText => ServiceProvider.Instance.DateTimeFormatter.FormatTime(Record.RecordTime);
    [ObservableProperty] private bool _isSwipeOpen;
    public string Icon => Record.RecordType switch
    {
        RecordType.Feed => "🍼",
        RecordType.Diaper => "💩",
        RecordType.Sleep => "😴",
        RecordType.Temperature => "🌡️",
        RecordType.Growth => "📏",
        RecordType.Supplement => "💊",
        RecordType.Pump => "🥛",
        RecordType.Complementary => "🥣",
        // 疫苗记录不在喂养页面显示，移除 Vaccine 图标分支
        RecordType.Abnormal => "⚠️",
        RecordType.Activity => "🏃",
        _ => "📝",
    };
    public string TitleText { get; }
    public string SubText { get; }

    public RecordDisplayItem(ChildRecord record)
    {
        Record = record;
        (TitleText, SubText) = BuildText(record);
    }

    private static (string, string) BuildText(ChildRecord r)
    {
        return r.RecordType switch
        {
            RecordType.Feed => r.RecordSubType == "breast"
                ? ($"母乳亲喂 {(r.LeftDurationSec > 0 ? "左" : "")}{(r.RightDurationSec > 0 ? "右" : "")}", $"{(r.DurationSec ?? 0) / 60}分钟")
                : ($"瓶喂{(r.RecordSubType == "expressed" ? "(母乳)" : "")}", $"{r.AmountMl ?? 0}ml"),
            RecordType.Diaper => (r.RecordSubType switch
            {
                "wet" => "小便",
                "dirty" => "大便",
                "both" => "大小便",
                _ => "换尿布",
            }, ""),
            RecordType.Sleep => ("睡眠", $"{(r.DurationSec ?? 0) / 60}分钟"),
            RecordType.Temperature => ("体温", $"{r.TemperatureValue:F1}℃"),
            RecordType.Growth => ("成长记录", $"{(r.HeightCm.HasValue ? $"身高{r.HeightCm}cm " : "")}{(r.WeightKg.HasValue ? $"体重{r.WeightKg}kg" : "")}"),
            RecordType.Supplement => ("补给", r.RecordSubType ?? ""),
            RecordType.Pump => ("吸奶", $"{r.AmountMl ?? 0}ml"),
            RecordType.Complementary => ("辅食", ""),
            // 疫苗记录仅在首页"疫苗追踪"模块展示，移除 BuildText 中的 Vaccine 分支
            RecordType.Abnormal => ("异常记录", BuildAbnormalText(r)),
            RecordType.Activity => ("活动", $"{(r.DurationSec ?? 0) / 60}分钟"),
            _ => (r.RecordType, ""),
        };
    }

    /// <summary>
    /// 构建异常记录的副标题文本：汇总体温、呼吸道症状、呕吐、其他描述、用药。
    /// 对齐小程序 feeding 页 abnormal 卡片的描述拼接逻辑。
    /// </summary>
    private static string BuildAbnormalText(ChildRecord r)
    {
        var parts = new List<string>();

        // 子类型中文映射（发烧/腹泻/呕吐/用药由 RecordService.AddAbnormal 写入 RecordSubType）
        var subTypeText = r.RecordSubType switch
        {
            "fever" => "发烧",
            "diarrhea" => "腹泻",
            "vomit" => "呕吐",
            "medicine" => "用药",
            _ => null,
        };
        if (subTypeText is not null) parts.Add(subTypeText);

        // 解析 PayloadJson 取详细症状
        AbnormalRecordDto? dto = null;
        if (!string.IsNullOrEmpty(r.PayloadJson) && r.PayloadJson != "{}")
        {
            try { dto = JsonSerializer.Deserialize<AbnormalRecordDto>(r.PayloadJson); } catch { }
        }

        if (r.TemperatureValue.HasValue) parts.Add($"{r.TemperatureValue:F1}℃");
        if (dto is not null)
        {
            if (dto.Respiratory.Count > 0) parts.Add("呼吸道：" + string.Join("、", dto.Respiratory));
            if (dto.Vomit && subTypeText != "呕吐") parts.Add("呕吐");
            if (dto.Medicine && subTypeText != "用药") parts.Add("已用药");
            if (!string.IsNullOrWhiteSpace(dto.Note)) parts.Add(dto.Note);
        }

        return parts.Count > 0 ? string.Join(" · ", parts) : "异常记录";
    }
}
