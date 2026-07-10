using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Shared.Dtos;
using ChildNotes.Shared.Constants;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

/// <summary>
/// RecordSheetViewModel 的公共基类。
/// 统一管理：11 个表单 VM 属性、ActiveType/IsVisible/SheetTitle 状态、
/// 类型→标题映射、Validate 调度、FillForm（编辑模式回填）。
/// </summary>
public abstract partial class RecordFormHostViewModel : ViewModelBase
{
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private string _activeType = string.Empty;
    [ObservableProperty] private string _sheetTitle = string.Empty;

    // 11 个表单 VM（统一实例化，派生类直接使用）
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

    protected readonly RecordService RecordService = ServiceProvider.Instance.RecordService;

    /// <summary>根据记录类型获取对应表单（用于统一 Validate 调度）。</summary>
    protected IRecordFormViewModel? GetActiveForm() => ActiveType switch
    {
        RecordType.Feed => FeedForm,
        RecordType.Diaper => DiaperForm,
        RecordType.Sleep => SleepForm,
        RecordType.Temperature => TemperatureForm,
        RecordType.Growth => GrowthForm,
        RecordType.Supplement => SupplementForm,
        RecordType.Pump => PumpForm,
        RecordType.Complementary => ComplementaryForm,
        RecordType.Abnormal => AbnormalForm,
        RecordType.Vaccine => VaccineForm,
        RecordType.Activity => ActivityForm,
        _ => null,
    };

    /// <summary>生成类型对应的标题（前缀如「记录」/「编辑」由派生类提供）。</summary>
    protected static string BuildTitle(string prefix, string type) => type switch
    {
        RecordType.Feed => $"{prefix}喂奶",
        RecordType.Diaper => $"{prefix}尿布",
        RecordType.Sleep => $"{prefix}睡眠",
        RecordType.Temperature => $"{prefix}体温",
        RecordType.Growth => $"{prefix}成长",
        RecordType.Supplement => $"{prefix}补给",
        RecordType.Pump => $"{prefix}吸奶",
        RecordType.Complementary => $"{prefix}辅食",
        RecordType.Abnormal => $"{prefix}异常",
        RecordType.Vaccine => $"{prefix}疫苗",
        RecordType.Activity => $"{prefix}活动",
        _ => $"{prefix}记录",
    };

    /// <summary>校验当前激活的表单；失败时设置 ErrorMessage 并返回 false。</summary>
    protected bool ValidateActiveForm()
    {
        var form = GetActiveForm();
        if (form is null) return true;
        if (!form.Validate(out var err))
        {
            ErrorMessage = err;
            return false;
        }
        return true;
    }

    /// <summary>
    /// 用现有记录填充对应类型的表单（编辑模式共用）。
    /// </summary>
    protected void FillForm(ChildRecord r)
    {
        var time = ServiceProvider.Instance.DateTimeFormatter.FormatTime(r.RecordTime);
        switch (r.RecordType)
        {
            case RecordType.Feed:
                FeedForm.FeedType = r.RecordSubType ?? "bottle";
                FeedForm.AmountText = r.AmountMl?.ToString() ?? string.Empty;
                FeedForm.LeftDurationText = (r.LeftDurationSec / 60).ToString();
                FeedForm.RightDurationText = (r.RightDurationSec / 60).ToString();
                FeedForm.Note = r.GetPayload<FeedRecordDto>()?.Note ?? string.Empty;
                FeedForm.DateText = ServiceProvider.Instance.DateTimeFormatter.FormatDate(r.RecordTime);
                FeedForm.TimeText = time;
                break;
            case RecordType.Diaper:
                DiaperForm.SelectType(r.RecordSubType ?? "wet");
                DiaperForm.DateText = ServiceProvider.Instance.DateTimeFormatter.FormatDate(r.RecordTime);
                DiaperForm.TimeText = time;
                break;
            case RecordType.Sleep:
                SleepForm.DateText = ServiceProvider.Instance.DateTimeFormatter.FormatDate(r.RecordTime);
                // 优先从 PayloadJson 回填 Start/EndTime，保持与存储数据一致；
                // 旧数据 PayloadJson 缺失或格式异常时回退到 RecordTime+DurationSec 重算
                var sleepDto = r.GetPayload<SleepRecordDto>();
                if (sleepDto is not null && !string.IsNullOrEmpty(sleepDto.StartTime))
                {
                    SleepForm.StartTimeText = sleepDto.StartTime;
                    SleepForm.EndTimeText = sleepDto.EndTime ?? string.Empty;
                }
                else
                {
                    SleepForm.StartTimeText = time;
                    var dur = r.DurationSec ?? 0;
                    SleepForm.EndTimeText = ServiceProvider.Instance.DateTimeFormatter.FormatTime(r.RecordTime.AddSeconds(dur));
                }
                var durMin = (r.DurationSec ?? 0) / 60;
                SleepForm.DurationText = durMin.ToString();
                break;
            case RecordType.Temperature:
                TemperatureForm.DateText = ServiceProvider.Instance.DateTimeFormatter.FormatDate(r.RecordTime);
                TemperatureForm.TemperatureText = r.TemperatureValue?.ToString("F1") ?? string.Empty;
                TemperatureForm.Note = string.Empty;
                TemperatureForm.TimeText = time;
                break;
            case RecordType.Growth:
                GrowthForm.DateText = ServiceProvider.Instance.DateTimeFormatter.FormatDate(r.RecordTime);
                GrowthForm.HeightText = r.HeightCm?.ToString() ?? string.Empty;
                GrowthForm.WeightText = r.WeightKg?.ToString() ?? string.Empty;
                GrowthForm.TimeText = time;
                break;
            case RecordType.Supplement:
                SupplementForm.DateText = ServiceProvider.Instance.DateTimeFormatter.FormatDate(r.RecordTime);
                SupplementForm.SwitchType(r.RecordSubType ?? "supplement");
                SupplementForm.TimeText = time;
                // 回填名称/剂量/单位/备注（从 PayloadJson 反序列化），否则编辑时表单为空无法修改
                var suppDto = r.GetPayload<SupplementRecordDto>();
                if (suppDto is not null)
                {
                    SupplementForm.Name = suppDto.Name ?? string.Empty;
                    SupplementForm.Dose = suppDto.Dose ?? string.Empty;
                    // 回填单位选中（旧数据 DoseUnit 可能为 null，保持默认选中）
                    SupplementForm.SelectDoseUnitByName(suppDto.DoseUnit);
                    SupplementForm.Note = suppDto.Note ?? string.Empty;
                }
                break;
            case RecordType.Pump:
                PumpForm.DateText = ServiceProvider.Instance.DateTimeFormatter.FormatDate(r.RecordTime);
                PumpForm.LeftDurationText = (r.LeftDurationSec / 60).ToString();
                PumpForm.RightDurationText = (r.RightDurationSec / 60).ToString();
                PumpForm.TotalAmountText = r.AmountMl?.ToString() ?? string.Empty;
                PumpForm.TimeText = time;
                break;
            case RecordType.Complementary:
                ComplementaryForm.DateText = ServiceProvider.Instance.DateTimeFormatter.FormatDate(r.RecordTime);
                ComplementaryForm.TimeText = time;
                var compDto = r.GetPayload<ComplementaryRecordDto>();
                if (compDto is not null)
                {
                    ComplementaryForm.FoodName = compDto.FoodName ?? string.Empty;
                    ComplementaryForm.AmountText = compDto.Amount ?? string.Empty;
                    ComplementaryForm.SelectAmountUnitByName(compDto.AmountUnit);
                    ComplementaryForm.Note = compDto.Note ?? string.Empty;
                }
                break;
            case RecordType.Abnormal:
                AbnormalForm.DateText = ServiceProvider.Instance.DateTimeFormatter.FormatDate(r.RecordTime);
                AbnormalForm.TemperatureText = r.TemperatureValue?.ToString("F1") ?? string.Empty;
                AbnormalForm.TimeText = time;
                AbnormalForm.Respiratory.Clear();
                AbnormalForm.VomitType = string.Empty;
                AbnormalForm.Other = string.Empty;
                AbnormalForm.Note = string.Empty;
                AbnormalForm.HasMedicine = false;
                var abnDto = r.GetPayload<AbnormalRecordDto>();
                if (abnDto is not null)
                {
                    foreach (var s in abnDto.Respiratory) AbnormalForm.Respiratory.Add(s);
                    if (abnDto.Vomit) AbnormalForm.VomitType = "喷射";
                    AbnormalForm.Other = abnDto.Note ?? string.Empty;
                    AbnormalForm.HasMedicine = abnDto.Medicine;
                }
                break;
            case RecordType.Vaccine:
                VaccineForm.RecordDateText = ServiceProvider.Instance.DateTimeFormatter.FormatDate(r.RecordTime);
                VaccineForm.RecordTimeText = time;
                break;
            case RecordType.Activity:
                ActivityForm.DateText = ServiceProvider.Instance.DateTimeFormatter.FormatDate(r.RecordTime);
                ActivityForm.DurationText = ((r.DurationSec ?? 0) / 60).ToString();
                ActivityForm.TimeText = time;
                break;
        }
    }

    [RelayCommand]
    protected void Close()
    {
        IsVisible = false;
        OnSheetClosed();
    }

    /// <summary>抽屉关闭后的扩展点（子类可覆写以触发额外逻辑，如通知 Shell 恢复 FAB）。</summary>
    protected virtual void OnSheetClosed() { }
}
