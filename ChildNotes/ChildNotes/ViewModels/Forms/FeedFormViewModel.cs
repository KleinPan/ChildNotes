using CommunityToolkit.Mvvm.ComponentModel;
using ChildNotes.Infrastructure;
using ChildNotes.Shared.Dtos;

namespace ChildNotes.ViewModels;

public partial class FeedFormViewModel : ObservableObject, IRecordFormViewModel
{
    [ObservableProperty] private string _feedType = "bottle";
    [ObservableProperty] private string _amountText = string.Empty;
    [ObservableProperty] private string _leftDurationText = string.Empty;
    [ObservableProperty] private string _rightDurationText = string.Empty;
    [ObservableProperty] private string _dateText = ServiceProvider.Instance.DateTimeFormatter.FormatDate(DateTime.Now);
    [ObservableProperty] private string _timeText = ServiceProvider.Instance.DateTimeFormatter.FormatTime(DateTime.Now);

    public void SwitchType(string type) => FeedType = type;

    public bool Validate(out string error)
    {
        if (FeedType == "breast")
        {
            if (!int.TryParse(LeftDurationText, out _) && !int.TryParse(RightDurationText, out _))
            {
                error = "请输入亲喂时长";
                return false;
            }
        }
        else
        {
            if (!int.TryParse(AmountText, out var amt) || amt <= 0)
            {
                error = "请输入奶量";
                return false;
            }
        }
        error = string.Empty;
        return true;
    }

    public FeedRecordDto BuildDto()
    {
        var dto = new FeedRecordDto { Type = FeedType, Time = $"{DateText} {TimeText}" };
        if (FeedType == "breast")
        {
            dto.LeftDuration = int.TryParse(LeftDurationText, out var l) ? l : 0;
            dto.RightDuration = int.TryParse(RightDurationText, out var r) ? r : 0;
            dto.LeftDurationSec = dto.LeftDuration * 60;
            dto.RightDurationSec = dto.RightDuration * 60;
        }
        else
        {
            dto.Amount = int.TryParse(AmountText, out var a) ? a : 0;
        }
        return dto;
    }
}
