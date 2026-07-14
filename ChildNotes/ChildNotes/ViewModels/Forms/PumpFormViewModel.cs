using CommunityToolkit.Mvvm.ComponentModel;
using ChildNotes.Infrastructure;
using ChildNotes.Services;
using ChildNotes.Shared.Dtos;

namespace ChildNotes.ViewModels;

public partial class PumpFormViewModel : ObservableObject, IRecordFormViewModel
{
    private readonly LocaleManager _locale = LocaleManager.Instance;

    [ObservableProperty] private string _dateText = ServiceProvider.Instance.DateTimeFormatter.FormatDate(DateTime.Now);
    [ObservableProperty] private string _leftDurationText = string.Empty;
    [ObservableProperty] private string _rightDurationText = string.Empty;
    [ObservableProperty] private string _leftAmountText = string.Empty;
    [ObservableProperty] private string _rightAmountText = string.Empty;
    [ObservableProperty] private string _totalAmountText = string.Empty;
    [ObservableProperty] private string _note = string.Empty;
    [ObservableProperty] private string _timeText = ServiceProvider.Instance.DateTimeFormatter.FormatTime(DateTime.Now);

    public bool Validate(out string error)
    {
        var hasTotal = int.TryParse(TotalAmountText, out var total) && total > 0;
        var hasLeft = int.TryParse(LeftAmountText, out var la) && la > 0;
        var hasRight = int.TryParse(RightAmountText, out var ra) && ra > 0;
        if (!hasTotal && !hasLeft && !hasRight)
        {
            error = _locale.GetString("Form_ErrPumpAmount", "请输入吸奶量");
            return false;
        }
        error = string.Empty;
        return true;
    }

    public PumpRecordDto BuildDto()
    {
        var dto = new PumpRecordDto
        {
            LeftDuration = int.TryParse(LeftDurationText, out var ld) ? ld : 0,
            RightDuration = int.TryParse(RightDurationText, out var rd) ? rd : 0,
            LeftAmount = int.TryParse(LeftAmountText, out var la) ? la : 0,
            RightAmount = int.TryParse(RightAmountText, out var ra) ? ra : 0,
            Note = Note,
            Time = $"{DateText} {TimeText}",
        };
        if (int.TryParse(TotalAmountText, out var total) && total > 0)
            dto.TotalAmount = total;
        else
            dto.TotalAmount = (dto.LeftAmount ?? 0) + (dto.RightAmount ?? 0);
        return dto;
    }
}
