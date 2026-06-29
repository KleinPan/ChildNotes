using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    [ObservableProperty] private string _selectedRange = "week";
    [ObservableProperty] private string _rangeLabel = "最近一周";
    [ObservableProperty] private string _totalText = "0次";
    [ObservableProperty] private string _averageText = "0次";
    [ObservableProperty] private string _averageLabel = "日均";
    [ObservableProperty] private string _maxText = "0次";
    [ObservableProperty] private string _currentTypeLabel = "喂养";
    [ObservableProperty] private string _currentTypeColor = "#07C160";

    public ObservableCollection<ChartBarItem> ChartBars { get; } = new();

    public void Load()
    {
        UpdateSelections();
        Rebuild();
    }

    private void UpdateSelections()
    {
        foreach (var t in TypeOptions) t.IsSelected = t.Key == SelectedType;
        foreach (var r in RangeOptions) r.IsSelected = r.Key == SelectedRange;
    }

    private (DateTime start, DateTime end) GetRange()
    {
        var today = DateTime.Today;
        return SelectedRange switch
        {
            "day" => (today, today),
            "threeDays" => (today.AddDays(-2), today),
            "week" => (today.AddDays(-6), today),
            "month" => (new DateTime(today.Year, today.Month, 1), today),
            "range" => (today.AddDays(-13), today),
            _ => (today.AddDays(-6), today),
        };
    }

    private void Rebuild()
    {
        var (start, end) = GetRange();
        var today = DateTime.Today;
        RangeLabel = SelectedRange switch
        {
            "day" => $"{today:yyyy年M月d日}",
            "threeDays" => "最近3天",
            "week" => "最近一周",
            "month" => $"{start:yyyy年M月}",
            "range" => $"{start:MM-dd} 至 {end:MM-dd}",
            _ => "最近一周",
        };
        AverageLabel = SelectedRange == "month" ? "月均" : "日均";

        var aggregates = _statsService.GetDailyAggregates(start, end);
        var typeOpt = TypeOptions.First(t => t.Key == SelectedType);
        CurrentTypeLabel = typeOpt.Label;
        CurrentTypeColor = typeOpt.Color;

        var values = aggregates.Select(a => new
        {
            a.Date,
            Value = StatisticsService.ExtractValue(a, SelectedType),
        }).ToList();

        var max = values.Max(v => v.Value);
        ChartBars.Clear();
        foreach (var v in values)
        {
            ChartBars.Add(new ChartBarItem
            {
                Label = SelectedRange == "month" ? $"{v.Date.Month}月" : v.Date.ToString("M/d"),
                ValueText = StatisticsService.FormatMetric(v.Value, SelectedType, typeOpt.Unit),
                HeightPct = max > 0 && v.Value > 0 ? Math.Max(4, (v.Value / max) * 100) : 0,
                BarColor = typeOpt.Color,
                IsToday = v.Date.Date == DateTime.Today,
            });
        }

        var total = values.Sum(v => v.Value);
        var avg = values.Count > 0 ? total / values.Count : 0;
        TotalText = StatisticsService.FormatMetric(total, SelectedType, typeOpt.Unit);
        AverageText = StatisticsService.FormatMetric(avg, SelectedType, typeOpt.Unit);
        MaxText = StatisticsService.FormatMetric(max, SelectedType, typeOpt.Unit);
    }

    [RelayCommand]
    private void SelectType(string key)
    {
        SelectedType = key;
        UpdateSelections();
        Rebuild();
    }

    [RelayCommand]
    private void SelectRange(string key)
    {
        SelectedRange = key;
        UpdateSelections();
        Rebuild();
    }
}

public sealed partial class StatTypeOption : ObservableObject
{
    public string Key { get; }
    public string Label { get; }
    public string Unit { get; }
    public string Color { get; }

    [ObservableProperty] private bool _isSelected;

    public StatTypeOption(string key, string label, string unit, string color)
    {
        Key = key; Label = label; Unit = unit; Color = color;
    }
}

public sealed partial class StatRangeOption : ObservableObject
{
    public string Key { get; }
    public string Label { get; }

    [ObservableProperty] private bool _isSelected;

    public StatRangeOption(string key, string label)
    {
        Key = key; Label = label;
    }
}

public sealed class ChartBarItem
{
    public string Label { get; set; } = string.Empty;
    public string ValueText { get; set; } = string.Empty;
    public double HeightPct { get; set; }
    public string BarColor { get; set; } = "#07C160";
    public bool IsToday { get; set; }
}
