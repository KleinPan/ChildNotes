using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Controls;
using ChildNotes.Infrastructure;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

public partial class StatisticsViewModel : ViewModelBase
{
    private readonly StatisticsService _statsService = ServiceProvider.Instance.StatisticsService;

    public ObservableCollection<StatTypeOption> TypeOptions { get; } = new()
    {
        new("feed", "喂养", "次", "#07C160"),
        new("milk", "奶量", "ml", "#10AEFF"),
        new("breast", "亲喂", "时长", "#35C2A1"),
        new("sleep", "睡眠", "时长", "#5B8DEF"),
        new("diaper", "尿布", "次", "#F59F00"),
        new("temperature", "体温", "次", "#FA5151"),
        new("supplement", "补给", "次", "#8B5CF6"),
        new("growth", "成长", "次", "#00A0A0"),
        new("pump", "吸奶", "ml", "#2F80ED"),
        new("complementary", "辅食", "次", "#E58A00"),
        new("abnormal", "异常", "次", "#D93025"),
        new("activity", "活动", "时长", "#0F9D58"),
        new("vaccine", "疫苗", "次", "#6A7CFF"),
    };

    public ObservableCollection<StatRangeOption> RangeOptions { get; } = new()
    {
        new("day", "日"),
        new("threeDays", "3天"),
        new("week", "一周"),
        new("month", "月"),
        new("range", "自定义"),
    };

    [ObservableProperty] private string _selectedType = "feed";
    [ObservableProperty] private string _selectedRange = "day";
    [ObservableProperty] private string _rangeLabel = "";
    [ObservableProperty] private bool _showMonthPicker = true;
    [ObservableProperty] private bool _showYearPicker;
    [ObservableProperty] private bool _showRangePickers;
    [ObservableProperty] private string _totalText = "0次";
    [ObservableProperty] private string _averageText = "0次";
    [ObservableProperty] private string _averageLabel = "日均";
    [ObservableProperty] private string _maxText = "0次";
    [ObservableProperty] private string _currentTypeLabel = "喂养";
    [ObservableProperty] private string _currentTypeColor = "#07C160";
    [ObservableProperty] private string _currentTypeUnit = "次";

    // ---- 图表数据 ----
    public ObservableCollection<ChartBarItem> ChartBars { get; } = new();
    /// <summary>Y轴标签（max / mid / 0）。</summary>
    public ObservableCollection<string> YAxisLabels { get; } = new();

    // ---- 日历热力图数据 ----
    public ObservableCollection<CalendarCellItem> CalendarCells { get; } = new();
    [ObservableProperty] private bool _calendarVisible;
    [ObservableProperty] private CalendarMode _calendarMode = CalendarMode.Day;
    [ObservableProperty] private string _calendarTitle = "";
    [ObservableProperty] private string _calendarSubtitle = "";

    // ---- 明细面板数据 ----
    public ObservableCollection<SummaryItem> SummaryItems { get; } = new();

    // ---- 日期选择器 ----
    [ObservableProperty] private string _selectedMonth = "";   // "yyyy-MM"
    [ObservableProperty] private string _selectedYear = "";    // "yyyy"
    [ObservableProperty] private DateTime _startDate = DateTime.Today.AddDays(-13);
    [ObservableProperty] private DateTime _endDate = DateTime.Today;

    // ---- 柱状图自适应 ----
    [ObservableProperty] private double _barWidth = 30;       // month=42, day=30, range=72/0(自适应)

    // ---- 自动滚动到今日 ----
    [ObservableProperty] private double _chartScrollLeft = 0;

    private static readonly string[] WeekDays = ["日", "一", "二", "三", "四", "五", "六"];

    public StatisticsViewModel()
    {
        var today = DateTime.Today;
        SelectedMonth = $"{today:yyyy-MM}";
        SelectedYear = $"{today:yyyy}";
    }

    public async Task LoadAsync()
    {
        UpdateSelections();
        var (start, end) = GetRange();
        var selectedType = SelectedType;
        var selectedRange = SelectedRange;
        var typeOpt = TypeOptions.First(t => t.Key == selectedType);

        // 后台线程：DB 查询 + 纯计算
        var snapshot = await Task.Run(() =>
        {
            var aggregates = _statsService.GetDailyAggregates(start, end);
            var valueList = aggregates.Select(a => (Date: a.Date, Value: StatisticsService.ExtractValue(a, selectedType))).ToList();
            var max = valueList.Count > 0 ? valueList.Max(v => v.Value) : 0;
            var total = valueList.Sum(v => v.Value);

            // 均值基数：对齐小程序 getAverageBase
            double avgBase = selectedRange switch
            {
                "month" => (SelectedYear == $"{DateTime.Today:yyyy}" ? DateTime.Today.Month : valueList.Count),
                "day" => (SelectedMonth == $"{DateTime.Today:yyyy-MM}" ? DateTime.Today.Day : valueList.Count),
                _ => valueList.Count,
            };
            var avg = avgBase > 0 ? total / avgBase : 0;

            // 柱状图数据
            var bars = BuildChartBars(valueList, max, selectedRange, typeOpt);
            // 日历热力图数据
            var calData = BuildCalendarData(valueList, max, selectedRange, typeOpt);
            // 明细面板数据
            var summary = BuildSummaryItems(selectedType, aggregates);

            return new Snapshot(bars, total, avg, max, calData, summary, valueList);
        });

        // UI 线程：仅属性赋值
        RangeLabel = selectedRange switch
        {
            "day" => $"{start:yyyy年M月d日}",
            "threeDays" => "最近3天",
            "week" => "最近一周",
            "month" => $"{start:yyyy年M月}",
            "range" => $"{start:MM-dd} 至 {end:MM-dd}",
            _ => "",
        };
        AverageLabel = selectedRange == "month" ? "月均" : "日均";
        CurrentTypeLabel = typeOpt.Label;
        CurrentTypeColor = typeOpt.Color;
        CurrentTypeUnit = typeOpt.Unit;

        // Y轴标签
        YAxisLabels.Clear();
        if (snapshot.Max > 0)
        {
            YAxisLabels.Add(StatisticsService.FormatMetric(snapshot.Max, selectedType, typeOpt.Unit));
            YAxisLabels.Add(StatisticsService.FormatMetric(snapshot.Max / 2.0, selectedType, typeOpt.Unit));
        }
        else { YAxisLabels.Add(""); }
        YAxisLabels.Add("0");

        // 柱状图
        ChartBars.Clear();
        foreach (var b in snapshot.Bars) ChartBars.Add(b);

        TotalText = StatisticsService.FormatMetric(snapshot.Total, selectedType, typeOpt.Unit);
        AverageText = StatisticsService.FormatMetric(snapshot.Avg, selectedType, typeOpt.Unit);
        MaxText = StatisticsService.FormatMetric(snapshot.Max, selectedType, typeOpt.Unit);

        // 日历热力图
        CalendarCells.Clear();
        foreach (var c in snapshot.CalendarResult.Cells) CalendarCells.Add(c);
        CalendarVisible = snapshot.CalendarResult.Data.Visible;
        CalendarMode = snapshot.CalendarResult.Data.Mode;
        CalendarTitle = snapshot.CalendarResult.Data.Title;
        CalendarSubtitle = snapshot.CalendarResult.Data.Subtitle;

        // 明细面板
        SummaryItems.Clear();
        foreach (var s in snapshot.Summary) SummaryItems.Add(s);

        // 柱宽自适应（对齐小程序 chart-mode-class）
        BarWidth = selectedRange switch
        {
            "month" => 42,
            "day" => 30,
            _ => snapshot.Bars.Count <= 7 ? 0 : 72,  // 0 表示 flex:1 自适应
        };

        // 自动滚动到今日（对齐小程序 getChartScrollLeft）
        if (selectedRange == "day" && SelectedMonth == $"{DateTime.Today:yyyy-MM}")
        {
            var todayIdx = snapshot.Values.FindIndex(v => v.Date.Date == DateTime.Today);
            if (todayIdx >= 0)
            {
                // 估算视口宽度约 280px，柱间距 ~36px
                var viewportPx = 280.0;
                var barPitchPx = BarWidth + 6;
                var todayCenterPx = (todayIdx + 0.5) * barPitchPx;
                ChartScrollLeft = Math.Max(0, todayCenterPx - viewportPx / 2);
            }
            else { ChartScrollLeft = 0; }
        }
        else { ChartScrollLeft = 0; }
    }

    // ---- 内部构建方法 ----

    private List<ChartBarItem> BuildChartBars(List<(DateTime Date, double Value)> valueList, double max, string range, StatTypeOption type)
    {
        return valueList.Select(v => new ChartBarItem
        {
            Label = range switch
            {
                "month" => $"{v.Date.Month}月",
                "day" => $"{v.Date.Day}",
                _ => v.Date.ToString("M/d"),
            },
            DateStr = v.Date.ToString("yyyy-MM-dd"),
            ValueText = StatisticsService.FormatMetric(v.Value, type.Key, type.Unit),
            HeightPct = max > 0 && v.Value > 0 ? Math.Max(4, (v.Value / max) * 100) : 0,
            BarColor = type.Color,
            IsToday = v.Date.Date == DateTime.Today,
        }).ToList();
    }

    private (CalendarData Data, List<CalendarCellItem> Cells) BuildCalendarData(
        List<(DateTime Date, double Value)> valueList, double max, string range, StatTypeOption type)
    {
        if (range == "day") return BuildDayCalendar(valueList, max, type);
        if (range == "month") return BuildMonthCalendar(valueList, max, type);
        return (new(false, CalendarMode.Day, "", ""), []);
    }

    private (CalendarData, List<CalendarCellItem>) BuildDayCalendar(List<(DateTime Date, double Value)> valueList, double max, StatTypeOption type)
    {
        var month = SelectedMonth;
        var ym = DateTime.ParseExact(month, "yyyy-MM", null);
        var firstDay = new DateTime(ym.Year, ym.Month, 1);
        var daysInMonth = firstDay.AddMonths(1).AddDays(-1).Day;
        var today = DateTime.Today;
        var todayStr = today.ToString("yyyy-MM-dd");

        var byDate = valueList.ToDictionary(v => v.Date.ToString("yyyy-MM-dd"), v => v.Value);

        var cells = new List<CalendarCellItem>();

        // 前导空单元格（对齐星期几）
        var startDow = (int)firstDay.DayOfWeek; // 0=Sun
        for (int i = 0; i < startDow; i++)
            cells.Add(new() { Key = $"empty-leading-{i}", IsEmpty = true });

        // 当月每天
        for (int d = 1; d <= daysInMonth; d++)
        {
            var dateStr = $"{month}-{d:D2}";
            var val = byDate.GetValueOrDefault(dateStr, 0);
            cells.Add(new()
            {
                Key = $"day-{dateStr}",
                Label = $"{d}",
                Date = dateStr,
                Value = val,
                Unit = type.Unit,
                IsToday = dateStr == todayStr,
                IsFuture = string.CompareOrdinal(dateStr, todayStr) > 0,
            });
        }

        // 尾部空单元格补满行
        while (cells.Count % 7 != 0)
            cells.Add(new() { Key = $"empty-trailing-{cells.Count}", IsEmpty = true });

        var title = $"{month} 日历统计";
        var subtitle = max > 0 ? $"峰值 {StatisticsService.FormatMetric(max, type.Key, type.Unit)}" : "暂无数据";

        return (new(true, CalendarMode.Day, title, subtitle), cells);
    }

    private (CalendarData, List<CalendarCellItem>) BuildMonthCalendar(List<(DateTime Date, double Value)> valueList, double max, StatTypeOption type)
    {
        var year = SelectedYear;
        var today = DateTime.Today;
        var todayMonth = $"{today:yyyy-MM}";

        var cells = new List<CalendarCellItem>();
        for (int m = 1; m <= 12; m++)
        {
            var monthKey = $"{year}-{m:D2}";
            var monthVal = valueList.Where(v => v.Date.ToString("yyyy-MM") == monthKey).Sum(v => v.Value);
            cells.Add(new()
            {
                Key = $"month-{monthKey}",
                Label = $"{m}月",
                Date = monthKey,
                Value = monthVal,
                Unit = type.Unit,
                IsToday = monthKey == todayMonth,
                IsFuture = string.CompareOrdinal(monthKey, todayMonth) > 0,
            });
        }

        var title = $"{year} 月历统计";
        var subtitle = max > 0 ? $"峰值 {StatisticsService.FormatMetric(max, type.Key, type.Unit)}" : "暂无数据";

        return (new(true, CalendarMode.Month, title, subtitle), cells);
    }

    private List<SummaryItem> BuildSummaryItems(string type, List<DayAggregate> list)
    {
        int SumInt(Func<DayAggregate, int> fn) => list.Sum(fn);
        double SumDbl(Func<DayAggregate, double> fn) => list.Sum(fn);

        return type switch
        {
            "feed" => [
                new("喂奶次数", $"{SumInt(d => d.FeedCount)}次"),
                new("总奶量", $"{SumInt(d => d.TotalMilk)}ml"),
                new("亲喂时长", FormatSeconds(SumDbl(d => d.BreastDurationSec))),
                new("统计范围", $"{list.Count}天"),
            ],
            "milk" => [
                new("总奶量", $"{SumInt(d => d.TotalMilk)}ml"),
                new("统计范围", $"{list.Count}天"),
                new("日均奶量", list.Count > 0 ? $"{SumInt(d => d.TotalMilk) / list.Count:F0}ml" : "0ml"),
                new("记录天数", $"{list.Count(d => d.FeedCount > 0)}天"),
            ],
            "breast" => [
                new("亲喂时长", FormatSeconds(SumDbl(d => d.BreastDurationSec))),
                new("统计范围", $"{list.Count}天"),
                new("日均时长", FormatSeconds(list.Count > 0 ? SumDbl(d => d.BreastDurationSec) / list.Count : 0)),
                new("记录天数", $"{list.Count(d => d.BreastDurationSec > 0)}天"),
            ],
            "sleep" => [
                new("总时长", FormatSeconds(SumDbl(d => d.SleepDurationSec))),
                new("统计范围", $"{list.Count}天"),
                new("日均时长", FormatSeconds(list.Count > 0 ? SumDbl(d => d.SleepDurationSec) / list.Count : 0)),
                new("记录天数", $"{list.Count(d => d.SleepDurationSec > 0)}天"),
            ],
            "diaper" => [
                new("尿布次数", $"{SumInt(d => d.DiaperCount)}次"),
                new("统计范围", $"{list.Count}天"),
                new("日均次数", list.Count > 0 ? $"{(double)SumInt(d => d.DiaperCount) / list.Count:F1}次" : "0次"),
                new("记录天数", $"{list.Count(d => d.DiaperCount > 0)}天"),
            ],
            "temperature" => [
                new("体温记录", $"{SumInt(d => d.TemperatureCount)}次"),
                new("统计范围", $"{list.Count}天"),
                new("记录天数", $"{list.Count(d => d.TemperatureCount > 0)}天"),
                new("平均次数", list.Count > 0 ? $"{(double)SumInt(d => d.TemperatureCount) / list.Count:F1}次" : "0次"),
            ],
            "supplement" => [
                new("补给次数", $"{SumInt(d => d.SupplementCount)}次"),
                new("统计范围", $"{list.Count}天"),
                new("日均次数", list.Count > 0 ? $"{(double)SumInt(d => d.SupplementCount) / list.Count:F1}次" : "0次"),
                new("记录天数", $"{list.Count(d => d.SupplementCount > 0)}天"),
            ],
            "growth" => [
                new("成长记录", $"{SumInt(d => d.GrowthCount)}次"),
                new("记录天数", $"{list.Count(d => d.GrowthCount > 0)}天"),
                new("统计范围", $"{list.Count}天"),
                new("全部记录", $"{SumInt(d => d.FeedCount + d.DiaperCount + d.TemperatureCount)}条"),
            ],
            "pump" => [
                new("吸奶总量", $"{SumInt(d => d.PumpTotalAmount)}ml"),
                new("统计范围", $"{list.Count}天"),
                new("日均奶量", list.Count > 0 ? $"{SumInt(d => d.PumpTotalAmount) / list.Count:F0}ml" : "0ml"),
                new("记录天数", $"{list.Count(d => d.PumpTotalAmount > 0)}天"),
            ],
            "complementary" => [
                new("辅食次数", $"{SumInt(d => d.ComplementaryCount)}次"),
                new("统计范围", $"{list.Count}天"),
                new("日均次数", list.Count > 0 ? $"{(double)SumInt(d => d.ComplementaryCount) / list.Count:F1}次" : "0次"),
                new("记录天数", $"{list.Count(d => d.ComplementaryCount > 0)}天"),
            ],
            "abnormal" => [
                new("异常次数", $"{SumInt(d => d.AbnormalCount)}次"),
                new("统计范围", $"{list.Count}天"),
                new("日均次数", list.Count > 0 ? $"{(double)SumInt(d => d.AbnormalCount) / list.Count:F1}次" : "0次"),
                new("记录天数", $"{list.Count(d => d.AbnormalCount > 0)}天"),
            ],
            "activity" => [
                new("活动时长", FormatSeconds(SumDbl(d => d.ActivityDurationSec))),
                new("统计范围", $"{list.Count}天"),
                new("日均时长", FormatSeconds(list.Count > 0 ? SumDbl(d => d.ActivityDurationSec) / list.Count : 0)),
                new("记录天数", $"{list.Count(d => d.ActivityDurationSec > 0)}天"),
            ],
            "vaccine" => [
                new("疫苗记录", $"{SumInt(d => d.VaccineCount)}次"),
                new("记录天数", $"{list.Count(d => d.VaccineCount > 0)}天"),
                new("统计范围", $"{list.Count}天"),
                new("全部记录", $"{SumInt(d => d.FeedCount + d.DiaperCount + d.TemperatureCount)}条"),
            ],
            _ => [
                new("记录数", $"{list.Count}天"),
                new("总计", "—"),
                new("平均", "—"),
                new("最高", "—"),
            ],
        };
    }

    private static string FormatSeconds(double sec)
    {
        var total = Math.Max(0, Math.Round(sec));
        if (total < 60) return $"{total}秒";
        var h = (int)(total / 3600);
        var m = (int)((total % 3600) / 60);
        var s = (int)(total % 60);
        if (h > 0) return m > 0 ? $"{h}时{m}分" : $"{h}时";
        return s > 0 ? $"{m}分{s}秒" : $"{m}分钟";
    }

    private (DateTime start, DateTime end) GetRange()
    {
        var today = DateTime.Today;
        return SelectedRange switch
        {
            "day" => (new DateTime(today.Year, today.Month, 1), today),
            "threeDays" => (today.AddDays(-2), today),
            "week" => (today.AddDays(-6), today),
            "month" => (new DateTime(today.Year, 1, 1), today),
            "range" => (StartDate, EndDate),
            _ => (today.AddDays(-6), today),
        };
    }

    private void UpdateSelections()
    {
        foreach (var t in TypeOptions) t.IsSelected = t.Key == SelectedType;
        foreach (var r in RangeOptions) r.IsSelected = r.Key == SelectedRange;

        // 日期选择器可见性：根据范围模式切换
        ShowMonthPicker = SelectedRange == "day";
        ShowYearPicker = SelectedRange == "month";
        ShowRangePickers = SelectedRange == "range";
    }

    [RelayCommand]
    private async Task SelectType(string key)
    {
        SelectedType = key;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task SelectRange(string key)
    {
        SelectedRange = key;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task SelectMonth(string month)
    {
        SelectedMonth = month;
        SelectedRange = "day";
        await LoadAsync();
    }

    [RelayCommand]
    private async Task SelectYear(string year)
    {
        SelectedYear = year;
        SelectedRange = "month";
        await LoadAsync();
    }

    // ---- 内部快照类型 ----
    private record Snapshot(
        List<ChartBarItem> Bars,
        double Total, double Avg, double Max,
        (CalendarData Data, List<CalendarCellItem> Cells) CalendarResult,
        List<SummaryItem> Summary,
        List<(DateTime Date, double Value)> Values);

    private record CalendarData(bool Visible, CalendarMode Mode, string Title, string Subtitle);
}

// ---- 数据模型 ----

public sealed partial class StatTypeOption : ObservableObject
{
    public string Key { get; }
    public string Label { get; }
    public string Unit { get; }
    public string Color { get; }
    [ObservableProperty] private bool _isSelected;
    public StatTypeOption(string key, string label, string unit, string color)
    { Key = key; Label = label; Unit = unit; Color = color; }
}

public sealed partial class StatRangeOption : ObservableObject
{
    public string Key { get; }
    public string Label { get; }
    [ObservableProperty] private bool _isSelected;
    public StatRangeOption(string key, string label)
    { Key = key; Label = label; }
}

public sealed class ChartBarItem
{
    public string Label { get; set; } = string.Empty;
    public string DateStr { get; set; } = string.Empty;
    public string ValueText { get; set; } = string.Empty;
    public double HeightPct { get; set; }
    public string BarColor { get; set; } = "#07C160";
    public bool IsToday { get; set; }
}

/// <summary>明细面板项。</summary>
public sealed class SummaryItem(string label, string value)
{
    public string Label { get; } = label;
    public string Value { get; set; } = value;
}
