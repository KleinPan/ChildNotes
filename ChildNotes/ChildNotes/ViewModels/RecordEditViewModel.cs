using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Models.Dtos;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

public partial class RecordEditViewModel : ViewModelBase
{
    private readonly RecordService _recordService = ServiceProvider.Instance.RecordService;

    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private string _sheetTitle = "编辑记录";
    [ObservableProperty] private string _activeType = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _showDeleteConfirm;

    public FeedFormViewModel FeedForm { get; } = new();
    public DiaperFormViewModel DiaperForm { get; } = new();
    public SleepFormViewModel SleepForm { get; } = new();
    public TemperatureFormViewModel TemperatureForm { get; } = new();
    public GrowthFormViewModel GrowthForm { get; } = new();
    public SupplementFormViewModel SupplementForm { get; } = new();
    public PumpFormViewModel PumpForm { get; } = new();
    public ComplementaryFormViewModel ComplementaryForm { get; } = new();
    public AbnormalFormViewModel AbnormalForm { get; } = new();
    public VaccineFormViewModel VaccineForm { get; } = new();
    public ActivityFormViewModel ActivityForm { get; } = new();

    private long _editingId;

    public event Action? Saved;
    public event Action? Deleted;

    public void Load(ChildRecord record)
    {
        _editingId = record.Id;
        ActiveType = record.RecordType;
        ErrorMessage = string.Empty;
        SheetTitle = record.RecordType switch
        {
            RecordType.Feed => "编辑喂奶",
            RecordType.Diaper => "编辑尿布",
            RecordType.Sleep => "编辑睡眠",
            RecordType.Temperature => "编辑体温",
            RecordType.Growth => "编辑成长",
            RecordType.Supplement => "编辑补给",
            RecordType.Pump => "编辑吸奶",
            RecordType.Complementary => "编辑辅食",
            RecordType.Abnormal => "编辑异常",
            RecordType.Vaccine => "编辑疫苗",
            RecordType.Activity => "编辑活动",
            _ => "编辑记录",
        };
        FillForm(record);
        IsVisible = true;
    }

    private void FillForm(ChildRecord r)
    {
        var time = r.RecordTime.ToString("HH:mm");
        switch (r.RecordType)
        {
            case RecordType.Feed:
                FeedForm.FeedType = r.RecordSubType ?? "bottle";
                FeedForm.AmountText = r.AmountMl?.ToString() ?? string.Empty;
                FeedForm.LeftDurationText = (r.LeftDurationSec / 60).ToString();
                FeedForm.RightDurationText = (r.RightDurationSec / 60).ToString();
                FeedForm.TimeText = time;
                break;
            case RecordType.Diaper:
                DiaperForm.SelectType(r.RecordSubType ?? "wet");
                DiaperForm.TimeText = time;
                break;
            case RecordType.Sleep:
                SleepForm.StartTimeText = time;
                var dur = r.DurationSec ?? 0;
                SleepForm.EndTimeText = r.RecordTime.AddSeconds(dur).ToString("HH:mm");
                SleepForm.DurationText = (dur / 60).ToString();
                break;
            case RecordType.Temperature:
                TemperatureForm.TemperatureText = r.TemperatureValue?.ToString("F1") ?? string.Empty;
                TemperatureForm.Note = string.Empty;
                TemperatureForm.TimeText = time;
                break;
            case RecordType.Growth:
                GrowthForm.HeightText = r.HeightCm?.ToString() ?? string.Empty;
                GrowthForm.WeightText = r.WeightKg?.ToString() ?? string.Empty;
                GrowthForm.TimeText = time;
                break;
            case RecordType.Supplement:
                SupplementForm.SwitchType(r.RecordSubType ?? "medicine");
                SupplementForm.TimeText = time;
                break;
            case RecordType.Pump:
                PumpForm.LeftDurationText = (r.LeftDurationSec / 60).ToString();
                PumpForm.RightDurationText = (r.RightDurationSec / 60).ToString();
                PumpForm.TotalAmountText = r.AmountMl?.ToString() ?? string.Empty;
                PumpForm.TimeText = time;
                break;
            case RecordType.Complementary:
                ComplementaryForm.TimeText = time;
                break;
            case RecordType.Abnormal:
                AbnormalForm.TemperatureText = r.TemperatureValue?.ToString("F1") ?? string.Empty;
                AbnormalForm.TimeText = time;
                break;
            case RecordType.Vaccine:
                VaccineForm.TimeText = time;
                break;
            case RecordType.Activity:
                ActivityForm.DurationText = ((r.DurationSec ?? 0) / 60).ToString();
                ActivityForm.TimeText = time;
                break;
        }
    }

    [RelayCommand]
    private void Close()
    {
        IsVisible = false;
    }

    [RelayCommand]
    private void Save()
    {
        ErrorMessage = string.Empty;
        var existing = _recordService.GetById(_editingId);
        if (existing is null)
        {
            ErrorMessage = "记录不存在";
            return;
        }

        var time = DateTime.Now.ToString("HH:mm");
        switch (ActiveType)
        {
            case RecordType.Feed:
                if (!FeedForm.Validate(out var feedErr)) { ErrorMessage = feedErr; return; }
                var feedDto = FeedForm.BuildDto();
                existing.RecordSubType = feedDto.Type;
                existing.AmountMl = feedDto.Amount;
                existing.LeftDurationSec = (feedDto.LeftDuration ?? 0) * 60;
                existing.RightDurationSec = (feedDto.RightDuration ?? 0) * 60;
                existing.DurationSec = ((feedDto.LeftDuration ?? 0) + (feedDto.RightDuration ?? 0)) * 60;
                existing.RecordTime = ParseTime(feedDto.Time, existing.RecordDate);
                break;
            case RecordType.Diaper:
                if (!DiaperForm.Validate(out var diaErr)) { ErrorMessage = diaErr; return; }
                var diaDto = DiaperForm.BuildDto();
                existing.RecordSubType = diaDto.Type;
                existing.RecordTime = ParseTime(diaDto.Time, existing.RecordDate);
                break;
            case RecordType.Sleep:
                if (!SleepForm.Validate(out var slpErr)) { ErrorMessage = slpErr; return; }
                var slpDto = SleepForm.BuildDto();
                existing.DurationSec = (slpDto.Duration ?? 0) * 60;
                existing.RecordTime = ParseTime(slpDto.StartTime, existing.RecordDate);
                break;
            case RecordType.Temperature:
                if (!TemperatureForm.Validate(out var tmpErr)) { ErrorMessage = tmpErr; return; }
                var tmpDto = TemperatureForm.BuildDto();
                existing.TemperatureValue = tmpDto.Temperature;
                existing.AbnormalFlag = tmpDto.Temperature >= 37.3m;
                existing.RecordTime = ParseTime(tmpDto.Time, existing.RecordDate);
                break;
            case RecordType.Growth:
                if (!GrowthForm.Validate(out var grwErr)) { ErrorMessage = grwErr; return; }
                var grwDto = GrowthForm.BuildDto();
                existing.HeightCm = grwDto.Height;
                existing.WeightKg = grwDto.Weight;
                existing.RecordTime = ParseTime(grwDto.Time, existing.RecordDate);
                break;
            case RecordType.Supplement:
                if (!SupplementForm.Validate(out var supErr)) { ErrorMessage = supErr; return; }
                var supDto = SupplementForm.BuildDto();
                existing.RecordSubType = supDto.Type;
                existing.RecordTime = ParseTime(supDto.Time, existing.RecordDate);
                break;
            case RecordType.Pump:
                if (!PumpForm.Validate(out var pmpErr)) { ErrorMessage = pmpErr; return; }
                var pmpDto = PumpForm.BuildDto();
                existing.LeftDurationSec = (pmpDto.LeftDuration ?? 0) * 60;
                existing.RightDurationSec = (pmpDto.RightDuration ?? 0) * 60;
                existing.AmountMl = pmpDto.TotalAmount;
                existing.RecordTime = ParseTime(pmpDto.Time, existing.RecordDate);
                break;
            case RecordType.Complementary:
                if (!ComplementaryForm.Validate(out var cmpErr)) { ErrorMessage = cmpErr; return; }
                var cmpDto = ComplementaryForm.BuildDto();
                existing.RecordSubType = cmpDto.Texture;
                existing.AbnormalFlag = cmpDto.Abnormal;
                existing.RecordTime = ParseTime(cmpDto.Time, existing.RecordDate);
                break;
            case RecordType.Abnormal:
                if (!AbnormalForm.Validate(out var abnErr)) { ErrorMessage = abnErr; return; }
                var abnDto = AbnormalForm.BuildDto();
                existing.TemperatureValue = abnDto.Temperature;
                existing.RecordTime = ParseTime(abnDto.Time, existing.RecordDate);
                break;
            case RecordType.Vaccine:
                if (!VaccineForm.Validate(out var vacErr)) { ErrorMessage = vacErr; return; }
                var vacDto = VaccineForm.BuildDto();
                existing.RecordTime = ParseTime(vacDto.Time, existing.RecordDate);
                break;
            case RecordType.Activity:
                if (!ActivityForm.Validate(out var actErr)) { ErrorMessage = actErr; return; }
                var actDto = ActivityForm.BuildDto();
                existing.RecordSubType = actDto.Category;
                existing.DurationSec = (actDto.Duration ?? 0) * 60;
                existing.RecordTime = ParseTime(actDto.Time, existing.RecordDate);
                break;
        }

        _recordService.Update(existing);
        IsVisible = false;
        Saved?.Invoke();
    }

    [RelayCommand]
    private void RequestDelete()
    {
        ShowDeleteConfirm = true;
    }

    [RelayCommand]
    private void CancelDelete()
    {
        ShowDeleteConfirm = false;
    }

    [RelayCommand]
    private void ConfirmDelete()
    {
        _recordService.Delete(_editingId);
        ShowDeleteConfirm = false;
        IsVisible = false;
        Deleted?.Invoke();
    }

    private static DateTime ParseTime(string timeStr, DateTime date)
    {
        if (TimeSpan.TryParse(timeStr, out var ts))
            return date.Date + ts;
        return date;
    }
}
