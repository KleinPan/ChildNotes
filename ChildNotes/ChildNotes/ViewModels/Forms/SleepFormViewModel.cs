using CommunityToolkit.Mvvm.ComponentModel;
using ChildNotes.Infrastructure;
using ChildNotes.Shared.Dtos;

namespace ChildNotes.ViewModels;

public partial class SleepFormViewModel : ObservableObject, IRecordFormViewModel
{
    [ObservableProperty] private string _dateText = ServiceProvider.Instance.DateTimeFormatter.FormatDate(DateTime.Now);
    [ObservableProperty] private string _startTimeText = ServiceProvider.Instance.DateTimeFormatter.FormatTime(DateTime.Now.AddHours(-1));
    [ObservableProperty] private string _endDateText = ServiceProvider.Instance.DateTimeFormatter.FormatDate(DateTime.Now);
    [ObservableProperty] private string _endTimeText = ServiceProvider.Instance.DateTimeFormatter.FormatTime(DateTime.Now);
    [ObservableProperty] private string _durationText = string.Empty;

    public bool Validate(out string error)
    {
        error = string.Empty;
        return true;
    }

    public SleepRecordDto BuildDto()
    {
        // StartTime/EndTime 存完整时间 "yyyy-MM-dd HH:mm"，支持跨日睡眠（起止日期可不同）
        var startTime = $"{DateText} {StartTimeText}";
        var endTime = $"{EndDateText} {EndTimeText}";
        var dto = new SleepRecordDto { StartTime = startTime, EndTime = endTime, Time = startTime };
        if (DateTime.TryParse(startTime, out var s) && DateTime.TryParse(endTime, out var e))
        {
            var diff = e - s;
            if (diff.TotalMinutes < 0) diff = diff.Add(TimeSpan.FromDays(1));
            dto.Duration = (int)diff.TotalMinutes;
        }
        return dto;
    }
}
