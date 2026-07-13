using CommunityToolkit.Mvvm.ComponentModel;
using ChildNotes.Infrastructure;
using ChildNotes.Shared.Dtos;

namespace ChildNotes.ViewModels;

public partial class ActivityFormViewModel : ObservableObject, IRecordFormViewModel
{
    [ObservableProperty] private string _dateText = ServiceProvider.Instance.DateTimeFormatter.FormatDate(DateTime.Now);
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _selectedCategory = "play";
    [ObservableProperty] private string _durationText = string.Empty;
    [ObservableProperty] private string _timeText = ServiceProvider.Instance.DateTimeFormatter.FormatTime(DateTime.Now);
    /// <summary>结束时间 "HH:mm"，可选。填了则用 结束-开始 算时长，覆盖 DurationText。</summary>
    [ObservableProperty] private string _endTimeText = string.Empty;

    public void SelectCategory(string c) => SelectedCategory = c;

    public bool Validate(out string error)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            error = "请输入活动名称";
            return false;
        }
        error = string.Empty;
        return true;
    }

    public ActivityRecordDto BuildDto()
    {
        var dto = new ActivityRecordDto
        {
            Name = Name,
            Category = SelectedCategory,
            Duration = int.TryParse(DurationText, out var d) ? d : 0,
            Time = $"{DateText} {TimeText}",
        };
        // 结束时间非空则存 EndTime，并用 (结束-开始) 覆盖 Duration
        if (!string.IsNullOrWhiteSpace(EndTimeText))
        {
            dto.EndTime = $"{DateText} {EndTimeText}";
            if (TryParseDurationMinutes(TimeText, EndTimeText, out var mins))
                dto.Duration = mins;
        }
        return dto;
    }

    /// <summary>从 "HH:mm" 起止时间算分钟差（支持跨日，结束<开始视为次日）。</summary>
    private static bool TryParseDurationMinutes(string startText, string endText, out int minutes)
    {
        minutes = 0;
        if (!TimeSpan.TryParse(startText, out var s) || !TimeSpan.TryParse(endText, out var e)) return false;
        var diff = e - s;
        if (diff < TimeSpan.Zero) diff = diff.Add(TimeSpan.FromDays(1)); // 跨日
        minutes = (int)diff.TotalMinutes;
        return true;
    }
}
