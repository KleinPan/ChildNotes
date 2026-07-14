using CommunityToolkit.Mvvm.ComponentModel;
using ChildNotes.Infrastructure;
using ChildNotes.Services;
using ChildNotes.Shared.Dtos;

namespace ChildNotes.ViewModels;

public partial class GrowthFormViewModel : ObservableObject, IRecordFormViewModel
{
    private readonly LocaleManager _locale = LocaleManager.Instance;

    [ObservableProperty] private string _dateText = ServiceProvider.Instance.DateTimeFormatter.FormatDate(DateTime.Now);
    [ObservableProperty] private string _heightText = string.Empty;
    [ObservableProperty] private string _weightText = string.Empty;
    [ObservableProperty] private string _note = string.Empty;
    [ObservableProperty] private string _timeText = ServiceProvider.Instance.DateTimeFormatter.FormatTime(DateTime.Now);

    public bool Validate(out string error)
    {
        var hasH = decimal.TryParse(HeightText, out _);
        var hasW = decimal.TryParse(WeightText, out _);
        if (!hasH && !hasW)
        {
            error = _locale.GetString("Form_ErrGrowthHeightWeight", "请至少输入身高或体重");
            return false;
        }
        error = string.Empty;
        return true;
    }

    public GrowthRecordDto BuildDto() => new()
    {
        Height = decimal.TryParse(HeightText, out var h) ? h : null,
        Weight = decimal.TryParse(WeightText, out var w) ? w : null,
        Time = $"{DateText} {TimeText}",
    };
}
