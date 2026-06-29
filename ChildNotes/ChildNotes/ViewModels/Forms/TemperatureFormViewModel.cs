using CommunityToolkit.Mvvm.ComponentModel;
using ChildNotes.Infrastructure;
using ChildNotes.Shared.Dtos;

namespace ChildNotes.ViewModels;

public partial class TemperatureFormViewModel : ObservableObject, IRecordFormViewModel
{
    [ObservableProperty] private string _temperatureText = string.Empty;
    [ObservableProperty] private bool _isAbnormal;
    [ObservableProperty] private string _note = string.Empty;
    [ObservableProperty] private string _timeText = ServiceProvider.Instance.DateTimeFormatter.FormatTime(DateTime.Now);

    public bool IsFeverWarning
    {
        get
        {
            if (decimal.TryParse(TemperatureText, out var t)) return t >= 37.3m;
            return false;
        }
    }

    public bool Validate(out string error)
    {
        if (!decimal.TryParse(TemperatureText, out var t) || t < 30 || t > 45)
        {
            error = "请输入有效体温（30-45℃）";
            return false;
        }
        IsAbnormal = t >= 37.3m;
        error = string.Empty;
        return true;
    }

    public TemperatureRecordDto BuildDto() => new()
    {
        Temperature = decimal.TryParse(TemperatureText, out var t) ? t : 0,
        IsAbnormal = IsAbnormal,
        Note = Note,
        Time = TimeText,
    };
}
