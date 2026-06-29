using CommunityToolkit.Mvvm.ComponentModel;
using ChildNotes.Infrastructure;
using ChildNotes.Shared.Dtos;

namespace ChildNotes.ViewModels;

public partial class SupplementFormViewModel : ObservableObject, IRecordFormViewModel
{
    [ObservableProperty] private string _suppType = "medicine";
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _dose = string.Empty;
    [ObservableProperty] private string _note = string.Empty;
    [ObservableProperty] private string _timeText = ServiceProvider.Instance.DateTimeFormatter.FormatTime(DateTime.Now);

    public void SwitchType(string type) => SuppType = type;

    public bool Validate(out string error)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            error = "请输入名称";
            return false;
        }
        error = string.Empty;
        return true;
    }

    public SupplementRecordDto BuildDto() => new()
    {
        Type = SuppType,
        Name = Name,
        Dose = Dose,
        Note = Note,
        Time = TimeText,
    };
}
