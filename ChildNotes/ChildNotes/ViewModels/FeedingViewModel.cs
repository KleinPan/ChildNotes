using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Media;
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
    /// <summary>当前选中的类型筛选（"all"/"feed"/"sleep"/"diaper"/"activity"/"other"）。默认"all"显示全部。</summary>
    [ObservableProperty] private string _selectedFilter = "all";

    /// <summary>展示用记录列表（根据 SelectedFilter 从 _allRecords 过滤）。</summary>
    public ObservableCollection<RecordDisplayItem> Records { get; } = new();
    /// <summary>全量记录缓存（未筛选），切换筛选时直接从此过滤，避免重复 DB 查询。</summary>
    private List<RecordDisplayItem> _allRecords = new();

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

        // 疫苗记录仅在首页"疫苗追踪"模块展示，不在喂养页面记录列表中显示
        // 活动记录已从首页移到喂养页时间轴展示（与喂奶/睡眠等并列）
        // 排序对齐小程序 _sort：睡眠记录用 StartTime（PayloadJson 内），其他用 RecordTime
        // 原因：历史数据中睡眠记录的 RecordTime 可能被存为记录创建时刻而非睡眠开始时间，
        // 导致睡眠记录在列表中排序错乱
        _allRecords = records.Where(x => x.RecordType != RecordType.Vaccine)
            .OrderBy(GetSortTime)
            .Select(r => new RecordDisplayItem(r))
            .ToList();
        ApplyFilter();
    }

    /// <summary>
    /// 根据 SelectedFilter 从 _allRecords 过滤记录到 Records。
    /// 切换筛选时不重新查 DB，直接从内存过滤，响应 &lt;5ms。
    /// </summary>
    private void ApplyFilter()
    {
        Records.Clear();
        IEnumerable<RecordDisplayItem> filtered = SelectedFilter switch
        {
            "feed" => _allRecords.Where(x => x.Record.RecordType == RecordType.Feed),
            "sleep" => _allRecords.Where(x => x.Record.RecordType == RecordType.Sleep),
            "diaper" => _allRecords.Where(x => x.Record.RecordType == RecordType.Diaper),
            "activity" => _allRecords.Where(x => x.Record.RecordType == RecordType.Activity),
            "other" => _allRecords.Where(x => !IsMainType(x.Record.RecordType)),
            _ => _allRecords,
        };
        foreach (var item in filtered)
            Records.Add(item);
    }

    /// <summary>主类型（有独立筛选 Tab 的类型）：feed/sleep/diaper/activity。</summary>
    private static bool IsMainType(string recordType)
        => recordType == RecordType.Feed || recordType == RecordType.Sleep
           || recordType == RecordType.Diaper || recordType == RecordType.Activity;

    /// <summary>切换类型筛选。由 View 层筛选条按钮调用。</summary>
    [RelayCommand]
    private void ApplyTypeFilter(string filter)
    {
        if (SelectedFilter == filter) return;
        SelectedFilter = filter;
        ApplyFilter();
    }

    /// <summary>
    /// 获取记录的排序时间。对齐小程序 feeding/index.js 的 _sort 逻辑：
    /// 睡眠记录用 StartTime（睡眠发生时刻），其他记录用 RecordTime。
    /// 历史数据中睡眠记录的 RecordTime 可能被存为记录创建时刻而非睡眠开始时间，
    /// 导致睡眠记录在列表中排序错乱，因此睡眠记录用 StartTime 作为排序依据。
    /// StartTime 现统一存 "yyyy-MM-dd HH:mm"；旧数据可能是 "HH:mm"，
    /// 此时结合 RecordDate 拼接为完整时间。
    /// </summary>
    private static DateTime GetSortTime(ChildRecord r)
    {
        if (r.RecordType == RecordType.Sleep)
        {
            var dto = r.GetPayload<SleepRecordDto>();
            if (dto is not null)
            {
                if (DateTime.TryParse(dto.StartTime, out var st)) return st;
                if (TimeSpan.TryParse(dto.StartTime, out var tp))
                    return r.RecordDate.Date + tp;
            }
        }
        return r.RecordTime;
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
            if (r == item)
            {
                r.IsSwipeOpen = !r.IsSwipeOpen;
                r.SwipeOffset = r.IsSwipeOpen ? -RecordDisplayItem.DeleteActionWidth : 0;
            }
            else
            {
                r.IsSwipeOpen = false;
                r.SwipeOffset = 0;
            }
        }
    }

    /// <summary>关闭所有已展开的滑动行（点击空白/切换页面时调用）。</summary>
    /// <returns>是否有任何行被关闭（用于消费点击事件）。</returns>
    public bool CloseAllSwipe()
    {
        var changed = false;
        foreach (var r in Records)
        {
            if (r.IsSwipeOpen || r.SwipeOffset != 0)
            {
                r.IsSwipeOpen = false;
                r.SwipeOffset = 0;
                changed = true;
            }
        }
        return changed;
    }

    public void EditRecord(RecordDisplayItem item)
    {
        foreach (var r in Records)
        {
            r.IsSwipeOpen = false;
            r.SwipeOffset = 0;
        }
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

    /// <summary>
    /// 卡片平移变换。X 为负值时卡片向左滑出，露出右侧删除按钮。
    /// 通过双向绑定到 Border.RenderTransform，由 FeedingView 的滑动手势处理逻辑更新。
    /// </summary>
    public TranslateTransform SwipeTransform { get; } = new();

    /// <summary>删除按钮宽度（逻辑像素），与 XAML 中底层 Border Width=72 一致。</summary>
    public const double DeleteActionWidth = 72;

    /// <summary>
    /// 当前滑动偏移量（负值）。设置时同步更新 SwipeTransform.X，驱动卡片平移。
    /// </summary>
    public double SwipeOffset
    {
        get => SwipeTransform.X;
        set => SwipeTransform.X = Math.Max(-DeleteActionWidth, Math.Min(0, value));
    }

    public string Icon => Record.RecordType switch
    {
        RecordType.Feed => "🍼",
        RecordType.Diaper => "💩",
        RecordType.Sleep => "😴",
        RecordType.Temperature => "🌡️",
        RecordType.Growth => "📏",
        RecordType.Supplement => "💊",
        RecordType.Water => "💧",
        RecordType.Pump => "🥛",
        RecordType.Complementary => "🥣",
        // 疫苗记录不在喂养页面显示，已移除 Vaccine 图标分支
        RecordType.Abnormal => "⚠️",
        RecordType.Activity => "🏃",
        _ => "📝",
    };
    public string TitleText { get; }
    public string SubText { get; }
    /// <summary>右侧强调文本（绿色显示，用于睡眠时长、补给类型标签等，对齐小程序 card-extra）</summary>
    public string ExtraText { get; }
    /// <summary>备注内容（浅灰背景块显示，对齐小程序 card-note）</summary>
    public string NoteText { get; }

    public RecordDisplayItem(ChildRecord record)
    {
        Record = record;
        (TitleText, SubText, ExtraText, NoteText) = BuildText(record);
    }

    private static (string Title, string Sub, string Extra, string Note) BuildText(ChildRecord r)
    {
        return r.RecordType switch
        {
            RecordType.Feed => r.RecordSubType == "breast"
                ? ($"母乳亲喂 {(r.LeftDurationSec > 0 ? "左" : "")}{(r.RightDurationSec > 0 ? "右" : "")}", $"{(r.DurationSec ?? 0) / 60}分钟", "", r.GetPayload<FeedRecordDto>()?.Note ?? "")
                : ($"瓶喂{(r.RecordSubType == "expressed" ? "(母乳)" : "")}", $"{r.AmountMl ?? 0}ml", "", r.GetPayload<FeedRecordDto>()?.Note ?? ""),
            RecordType.Diaper => (r.RecordSubType switch
            {
                "wet" => "小便",
                "dirty" => "大便",
                "both" => "大小便",
                _ => "换尿布",
            }, "", "", r.GetPayload<DiaperRecordDto>()?.Consistency ?? ""),
            RecordType.Sleep => BuildSleepText(r),
            RecordType.Temperature => ("体温", $"{r.TemperatureValue:F1}℃", "", ""),
            RecordType.Growth => ("成长记录", $"{(r.HeightCm.HasValue ? $"身高{r.HeightCm}cm " : "")}{(r.WeightKg.HasValue ? $"体重{r.WeightKg}kg" : "")}", "", ""),
            RecordType.Supplement => BuildSupplementText(r),
            RecordType.Water => ("喝水", r.AmountMl.HasValue ? $"{r.AmountMl}ml" : "", "饮水", ""),
            RecordType.Pump => ("吸奶", $"{r.AmountMl ?? 0}ml", "", ""),
            RecordType.Complementary => BuildComplementaryText(r),
            // 疫苗记录仅在首页"疫苗追踪"模块展示，移除 BuildText 中的 Vaccine 分支
            RecordType.Abnormal => ("异常记录", BuildAbnormalText(r), "", ""),
            RecordType.Activity => BuildActivityText(r),
            _ => (r.RecordType, "", "", ""),
        };
    }

    /// <summary>
    /// 睡眠记录：desc 显示"开始 → 结束"，extra（绿色）显示"共 X小时Y分钟"。
    /// 对齐小程序 feeding 页 sleep 卡片的 desc/extra 拆分显示逻辑。
    /// </summary>
    private static (string Title, string Sub, string Extra, string Note) BuildSleepText(ChildRecord r)
    {
        string sub = "";
        string extra = "";
        var dto = r.GetPayload<SleepRecordDto>();
        var s = FormatTimeFromPayload(dto?.StartTime);
        var e = FormatTimeFromPayload(dto?.EndTime);
        if (!string.IsNullOrEmpty(s))
        {
            sub = !string.IsNullOrEmpty(e) ? $"{s} → {e}" : $"{s} 开始";
        }
        // 时长（绿色）
        var totalMin = (r.DurationSec ?? 0) / 60;
        if (totalMin > 0)
        {
            extra = totalMin >= 60 ? $"共 {totalMin / 60}小时{totalMin % 60}分钟" : $"共 {totalMin}分钟";
        }
        return ("睡眠", sub, extra, "");
    }

    /// <summary>
    /// 活动记录：title 显示活动名称，sub 显示"开始→结束"时间（有 EndTime 时），extra（绿色）显示时长。
    /// 对齐睡眠记录的起止时间展示格式。
    /// </summary>
    private static (string Title, string Sub, string Extra, string Note) BuildActivityText(ChildRecord r)
    {
        var dto = r.GetPayload<ActivityRecordDto>();
        var name = !string.IsNullOrWhiteSpace(dto?.Name) ? dto.Name : "活动";
        string sub = "";
        var s = r.RecordTime.ToString("HH:mm");
        var e = FormatTimeFromPayload(dto?.EndTime);
        if (!string.IsNullOrEmpty(e))
            sub = $"{s} → {e}";
        // 时长（绿色）
        var totalMin = (r.DurationSec ?? 0) / 60;
        string extra = totalMin > 0
            ? (totalMin >= 60 ? $"共 {totalMin / 60}小时{totalMin % 60}分钟" : $"共 {totalMin}分钟")
            : "";
        return (name, sub, extra, "");
    }

    /// <summary>
    /// 从 Payload 中提取 "HH:mm" 时间文本，兼容三种历史存储格式：
    /// 1) "HH:mm"（表单创建）
    /// 2) ISO 8601（WakeUpSleep 写入的 "O" 格式，如 "2026-07-10T06:00:00+08:00"）
    /// 3) "yyyy-MM-dd HH:mm"（后端同步下发的 displayStartTime/displayEndTime）
    /// </summary>
    private static string FormatTimeFromPayload(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        // 优先尝试解析为 DateTime，可覆盖 ISO 与 "yyyy-MM-dd HH:mm" 两种格式
        if (DateTime.TryParse(raw, out var dt)) return dt.ToString("HH:mm");
        // 回退：截取前 5 字符（"HH:mm" 格式）
        return raw.Length >= 5 ? raw[..5] : raw;
    }

    /// <summary>
    /// 补给/用药记录：title 显示实际名称，sub 显示剂量，extra（绿色）显示"补充剂"/"用药"类型标签。
    /// 对齐小程序 feeding 页 supplement 卡片的渲染逻辑。
    /// </summary>
    private static (string Title, string Sub, string Extra, string Note) BuildSupplementText(ChildRecord r)
    {
        var isMedicine = r.RecordSubType == "medicine";
        var dto = r.GetPayload<SupplementRecordDto>();
        var name = dto?.Name;
        var title = !string.IsNullOrWhiteSpace(name)
            ? name
            : (isMedicine ? "用药记录" : "补充剂记录");
        // 剂量展示：Dose + DoseUnit 拼接；兼容旧数据（Dose 含单位文本、DoseUnit 为 null）
        var sub = FormatDoseDisplay(dto?.Dose, dto?.DoseUnit);
        var extra = isMedicine ? "用药" : "补充剂";
        var note = !string.IsNullOrWhiteSpace(dto?.Note) ? dto!.Note! : "";
        return (title, sub, extra, note);
    }

    /// <summary>
    /// 格式化剂量展示：0.5+包→半包，1+粒→1粒，5+ml→5ml。
    /// 兼容旧数据：DoseUnit 为 null 时直接显示 Dose 原文。
    /// </summary>
    private static string FormatDoseDisplay(string? dose, string? doseUnit)
    {
        if (string.IsNullOrWhiteSpace(dose)) return "";
        if (string.IsNullOrEmpty(doseUnit)) return dose!;
        // "0.5"+"包"→"半包"（更符合中文习惯）
        if (dose == "0.5") return "半" + doseUnit;
        return dose + doseUnit;
    }

    /// <summary>
    /// 辅食记录：title 显示食物名称，sub 显示量，note 显示备注。
    /// </summary>
    private static (string Title, string Sub, string Extra, string Note) BuildComplementaryText(ChildRecord r)
    {
        var dto = r.GetPayload<ComplementaryRecordDto>();
        var title = !string.IsNullOrWhiteSpace(dto?.FoodName)
            ? dto!.FoodName!
            : "辅食";
        var parts = new List<string>();
        if (dto?.FoodTypes.Count > 0) parts.Add(string.Join("、", dto!.FoodTypes));
        if (!string.IsNullOrWhiteSpace(dto?.Amount))
            parts.Add($"{dto!.Amount}{dto.AmountUnit ?? ""}");
        var sub = string.Join(" · ", parts);
        var note = !string.IsNullOrWhiteSpace(dto?.Note) ? dto!.Note! : "";
        return (title, sub, "", note);
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
