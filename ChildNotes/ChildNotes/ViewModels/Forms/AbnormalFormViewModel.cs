using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ChildNotes.Infrastructure;
using ChildNotes.Services;
using ChildNotes.Shared.Constants;
using ChildNotes.Shared.Dtos;

namespace ChildNotes.ViewModels;

public partial class AbnormalFormViewModel : ObservableObject, IRecordFormViewModel
{
    private readonly LocaleManager _locale = LocaleManager.Instance;

    // ===== 呼吸道症状（多选） =====
    public ObservableCollection<string> Respiratory { get; } = new();

    // ===== 消化道异常 =====
    [ObservableProperty] private string _vomitType = string.Empty; // "" / "溢奶" / "喷射"

    // ===== 其他异常自由描述 =====
    [ObservableProperty] private string _other = string.Empty;

    // ===== 备注 =====
    [ObservableProperty] private string _note = string.Empty;

    // ===== 体温（选填，与小程序一致：如有发热可一并记录） =====
    [ObservableProperty] private string _temperatureText = string.Empty;

    // ===== 是否用药 =====
    [ObservableProperty] private bool _hasMedicine;

    [ObservableProperty] private string _dateText = ServiceProvider.Instance.DateTimeFormatter.FormatDate(DateTime.Now);
    [ObservableProperty] private string _timeText = ServiceProvider.Instance.DateTimeFormatter.FormatTime(DateTime.Now);

    public bool IsFeverWarning
    {
        get
        {
            if (decimal.TryParse(TemperatureText, out var t)) return t >= HealthConstants.FeverThreshold;
            return false;
        }
    }

    partial void OnTemperatureTextChanged(string value) => OnPropertyChanged(nameof(IsFeverWarning));

    /// <summary>切换呼吸道症状选中状态。</summary>
    public void ToggleRespiratory(string symptom)
    {
        if (string.IsNullOrEmpty(symptom)) return;
        if (Respiratory.Contains(symptom)) Respiratory.Remove(symptom);
        else Respiratory.Add(symptom);
    }

    /// <summary>选择呕吐类型：再次点击同一项取消。</summary>
    public void SelectVomit(string type)
    {
        VomitType = VomitType == type ? string.Empty : type;
    }

    public bool Validate(out string error)
    {
        if (Respiratory.Count == 0
            && string.IsNullOrEmpty(VomitType)
            && string.IsNullOrWhiteSpace(Other)
            && string.IsNullOrWhiteSpace(Note)
            && !HasMedicine
            && string.IsNullOrWhiteSpace(TemperatureText))
        {
            error = _locale.GetString("Form_ErrAbnormalEmpty", "请至少填写一项异常症状");
            return false;
        }
        if (!string.IsNullOrWhiteSpace(TemperatureText))
        {
            if (!decimal.TryParse(TemperatureText, out var t) || t < 30 || t > 45)
            {
                error = _locale.GetString("Form_ErrTemperatureRange", "请输入有效体温（30-45℃）");
                return false;
            }
        }
        error = string.Empty;
        return true;
    }

    public AbnormalRecordDto BuildDto()
    {
        // 注：体温 ≥ 38℃ 时 RecordService.AddAbnormal 会自动将 RecordSubType 设为 "fever"
        return new AbnormalRecordDto
        {
            Temperature = decimal.TryParse(TemperatureText, out var t) ? t : null,
            Respiratory = Respiratory.ToList(),
            Diarrhea = new List<string>(),
            Vomit = !string.IsNullOrEmpty(VomitType),
            Medicine = HasMedicine,
            Note = string.IsNullOrWhiteSpace(Other) ? Note : Other,
            Time = $"{DateText} {TimeText}",
        };
    }
}
