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

    public ActivityRecordDto BuildDto() => new()
    {
        Name = Name,
        Category = SelectedCategory,
        Duration = int.TryParse(DurationText, out var d) ? d : 0,
        Time = $"{DateText} {TimeText}",
    };
}
