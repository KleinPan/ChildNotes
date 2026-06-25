using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Models.Dtos;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

public partial class RecordSheetViewModel : ViewModelBase
{
    private readonly RecordService _recordService = ServiceProvider.Instance.RecordService;

    [ObservableProperty] private string _activeType = string.Empty;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private string _errorMessage = string.Empty;

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
    public MaternalFoodFormViewModel MaternalFoodForm { get; } = new();

    public event Action? Saved;

    public void Open(string type)
    {
        ActiveType = type;
        Title = type switch
        {
            RecordType.Feed => "记录喂奶",
            RecordType.Diaper => "换尿布",
            RecordType.Sleep => "记录睡眠",
            RecordType.Temperature => "记录体温",
            RecordType.Growth => "记录成长",
            RecordType.Supplement => "记录补给",
            RecordType.Pump => "记录吸奶",
            RecordType.Complementary => "记录辅食",
            RecordType.Abnormal => "记录异常",
            RecordType.Vaccine => "记录疫苗",
            RecordType.Activity => "记录活动",
            RecordType.MaternalFood => "记录妈妈饮食",
            _ => "记录",
        };
        ErrorMessage = string.Empty;
        IsVisible = true;
        // 疫苗类型需要加载时间轴
        if (type == RecordType.Vaccine)
        {
            VaccineForm.Load();
        }
    }

    /// <summary>疫苗专用：标记某剂次为「已打」并保存</summary>
    public bool MarkVaccineDone(VaccinePlanView plan)
    {
        var dto = VaccineForm.MarkDone(plan);
        if (dto is null) return false;
        try
        {
            _recordService.AddVaccine(dto);
            VaccineForm.Load(); // 刷新时间轴状态
            Saved?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"保存失败：{ex.Message}";
            return false;
        }
    }

    /// <summary>疫苗专用：标记某剂次为「跳过」并保存</summary>
    public bool MarkVaccineSkipped(VaccinePlanView plan)
    {
        var dto = VaccineForm.MarkSkipped(plan);
        if (dto is null) return false;
        try
        {
            _recordService.AddVaccine(dto);
            VaccineForm.Load();
            Saved?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"保存失败：{ex.Message}";
            return false;
        }
    }

    /// <summary>疫苗专用：添加自定义疫苗到时间轴</summary>
    public (bool Ok, string Error) AddCustomVaccine()
    {
        var (ok, error) = VaccineForm.AddCustomVaccine();
        if (!ok) ErrorMessage = error;
        else ErrorMessage = string.Empty;
        return (ok, error);
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
        try
        {
            switch (ActiveType)
            {
                case RecordType.Feed:
                    if (!FeedForm.Validate(out var feedErr)) { ErrorMessage = feedErr; return; }
                    _recordService.AddFeed(FeedForm.BuildDto());
                    break;
                case RecordType.Diaper:
                    if (!DiaperForm.Validate(out var diaperErr)) { ErrorMessage = diaperErr; return; }
                    _recordService.AddDiaper(DiaperForm.BuildDto());
                    break;
                case RecordType.Sleep:
                    if (!SleepForm.Validate(out var sleepErr)) { ErrorMessage = sleepErr; return; }
                    _recordService.AddSleep(SleepForm.BuildDto());
                    break;
                case RecordType.Temperature:
                    if (!TemperatureForm.Validate(out var tempErr)) { ErrorMessage = tempErr; return; }
                    _recordService.AddTemperature(TemperatureForm.BuildDto());
                    break;
                case RecordType.Growth:
                    if (!GrowthForm.Validate(out var growthErr)) { ErrorMessage = growthErr; return; }
                    _recordService.AddGrowth(GrowthForm.BuildDto());
                    break;
                case RecordType.Supplement:
                    if (!SupplementForm.Validate(out var suppErr)) { ErrorMessage = suppErr; return; }
                    _recordService.AddSupplement(SupplementForm.BuildDto());
                    break;
                case RecordType.Pump:
                    if (!PumpForm.Validate(out var pumpErr)) { ErrorMessage = pumpErr; return; }
                    _recordService.AddPump(PumpForm.BuildDto());
                    break;
                case RecordType.Complementary:
                    if (!ComplementaryForm.Validate(out var compErr)) { ErrorMessage = compErr; return; }
                    _recordService.AddComplementary(ComplementaryForm.BuildDto());
                    break;
                case RecordType.Abnormal:
                    if (!AbnormalForm.Validate(out var abnErr)) { ErrorMessage = abnErr; return; }
                    _recordService.AddAbnormal(AbnormalForm.BuildDto());
                    break;
                case RecordType.Vaccine:
                    if (!VaccineForm.Validate(out var vacErr)) { ErrorMessage = vacErr; return; }
                    _recordService.AddVaccine(VaccineForm.BuildDto());
                    break;
                case RecordType.Activity:
                    if (!ActivityForm.Validate(out var actErr)) { ErrorMessage = actErr; return; }
                    _recordService.AddActivity(ActivityForm.BuildDto());
                    break;
                case RecordType.MaternalFood:
                    if (!MaternalFoodForm.Validate(out var mfErr)) { ErrorMessage = mfErr; return; }
                    _recordService.AddMaternalFood(MaternalFoodForm.BuildDto());
                    break;
            }
            IsVisible = false;
            Saved?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"保存失败：{ex.Message}";
        }
    }
}
