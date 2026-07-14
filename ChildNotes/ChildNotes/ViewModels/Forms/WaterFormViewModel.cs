using CommunityToolkit.Mvvm.ComponentModel;
using ChildNotes.Infrastructure;
using ChildNotes.Services;
using ChildNotes.Shared.Dtos;

namespace ChildNotes.ViewModels;

/// <summary>
/// 喝水记录表单 VM。字段：AmountMl / Note / Date / Time。
/// 历史仅通过 AI 记创建，无手动入口，导致编辑时表单空白。补齐表单与编辑支持。
/// </summary>
public partial class WaterFormViewModel : ObservableObject, IRecordFormViewModel
{
    private readonly LocaleManager _locale = LocaleManager.Instance;

    [ObservableProperty] private string _dateText = ServiceProvider.Instance.DateTimeFormatter.FormatDate(DateTime.Now);
    [ObservableProperty] private string _amountText = string.Empty;
    [ObservableProperty] private string _note = string.Empty;
    [ObservableProperty] private string _timeText = ServiceProvider.Instance.DateTimeFormatter.FormatTime(DateTime.Now);

    public bool Validate(out string error)
    {
        if (!int.TryParse(AmountText, out var ml) || ml <= 0)
        {
            error = _locale.GetString("Form_ErrWaterAmount", "请输入喝水量（ml）");
            return false;
        }
        error = string.Empty;
        return true;
    }

    public WaterRecordDto BuildDto() => new()
    {
        AmountMl = int.TryParse(AmountText, out var ml) ? ml : null,
        Note = string.IsNullOrWhiteSpace(Note) ? null : Note,
        Time = $"{DateText} {TimeText}",
    };
}
