using CommunityToolkit.Mvvm.ComponentModel;
using ChildNotes.Infrastructure;
using ChildNotes.Models.Dtos;

namespace ChildNotes.ViewModels;

public partial class SleepFormViewModel : ObservableObject, IRecordFormViewModel
{
    [ObservableProperty] private string _startTimeText = ServiceProvider.Instance.DateTimeFormatter.FormatTime(DateTime.Now.AddHours(-1));
    [ObservableProperty] private string _endTimeText = ServiceProvider.Instance.DateTimeFormatter.FormatTime(DateTime.Now);
    [ObservableProperty] private string _durationText = string.Empty;

    public bool Validate(out string error)
    {
        error = string.Empty;
        return true;
    }

    public SleepRecordDto BuildDto()
    {
        var dto = new SleepRecordDto { StartTime = StartTimeText, EndTime = EndTimeText, Time = StartTimeText };
        if (TimeSpan.TryParse(StartTimeText, out var s) && TimeSpan.TryParse(EndTimeText, out var e))
        {
            var diff = e - s;
            if (diff.TotalMinutes < 0) diff = diff.Add(TimeSpan.FromDays(1));
            dto.Duration = (int)diff.TotalMinutes;
        }
        return dto;
    }
}
