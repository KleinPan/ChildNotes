using CommunityToolkit.Mvvm.ComponentModel;
using ChildNotes.Infrastructure;
using ChildNotes.Models.Dtos;

namespace ChildNotes.ViewModels;

public partial class DiaperFormViewModel : ObservableObject, IRecordFormViewModel
{
    [ObservableProperty] private string _diaperType = "wet";
    [ObservableProperty] private string _selectedUrineColor = string.Empty;
    [ObservableProperty] private string _selectedStoolColor = string.Empty;
    [ObservableProperty] private string _selectedConsistency = string.Empty;
    [ObservableProperty] private bool _abnormal;
    [ObservableProperty] private string _timeText = ServiceProvider.Instance.DateTimeFormatter.FormatTime(DateTime.Now);

    public void SelectType(string type) => DiaperType = type;

    public bool Validate(out string error)
    {
        error = string.Empty;
        return true;
    }

    public DiaperRecordDto BuildDto() => new()
    {
        Type = DiaperType,
        UrineColor = SelectedUrineColor,
        Color = SelectedStoolColor,
        Consistency = SelectedConsistency,
        Abnormal = Abnormal,
        Time = TimeText,
    };
}
