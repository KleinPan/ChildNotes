using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

public partial class FeedingViewModel : ViewModelBase, IActivatable
{
    private readonly RecordService _recordService = ServiceProvider.Instance.RecordService;
    private readonly StatisticsService _statsService = ServiceProvider.Instance.StatisticsService;

    [ObservableProperty] private DateTime _selectedDate = DateTime.Today;
    [ObservableProperty] private string _displayDate = string.Empty;
    [ObservableProperty] private DayStats? _dayStats;
    [ObservableProperty] private bool _isToday = true;
    [ObservableProperty] private bool _showDeleteConfirm;
    [ObservableProperty] private string _deleteItemTitle = string.Empty;
    [ObservableProperty] private string _toastMessage = string.Empty;
    [ObservableProperty] private bool _showToast;

    public ObservableCollection<RecordDisplayItem> Records { get; } = new();
    public RecordEditViewModel RecordEdit { get; } = new();

    private long _deletingRecordId;

    public FeedingViewModel()
    {
        RecordEdit.Saved += () => { LoadData(); ShowToastMessage("已更新记录"); };
        RecordEdit.Deleted += () => { LoadData(); ShowToastMessage("已删除记录"); };
    }

    public void Activate()
    {
        LoadData();
    }

    private void LoadData()
    {
        DisplayDate = SelectedDate.ToString("M月d日") + " " + GetWeekday(SelectedDate);
        IsToday = SelectedDate.Date == DateTime.Today;
        DayStats = _statsService.GetDayStats(SelectedDate);

        Records.Clear();
        var records = _recordService.GetByDate(SelectedDate);
        foreach (var r in records.OrderBy(x => x.RecordTime))
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
        LoadData();
    }

    [RelayCommand]
    private void NextDay()
    {
        if (SelectedDate < DateTime.Today)
        {
            SelectedDate = SelectedDate.AddDays(1);
            LoadData();
        }
    }

    [RelayCommand]
    private void GoToday()
    {
        SelectedDate = DateTime.Today;
        LoadData();
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
        RecordEdit.Load(item.Record);
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
        LoadData();
        ShowToastMessage("已删除记录");
    }

    private async void ShowToastMessage(string msg)
    {
        ToastMessage = msg;
        ShowToast = true;
        await Task.Delay(2000);
        ShowToast = false;
    }
}

public sealed partial class RecordDisplayItem : ObservableObject
{
    public ChildRecord Record { get; }
    public string TimeText => Record.RecordTime.ToString("HH:mm");
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
        RecordType.Vaccine => "💉",
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
            RecordType.Vaccine => ("疫苗", ""),
            RecordType.Abnormal => ("异常记录", r.RecordSubType ?? ""),
            RecordType.Activity => ("活动", $"{(r.DurationSec ?? 0) / 60}分钟"),
            _ => (r.RecordType, ""),
        };
    }
}
