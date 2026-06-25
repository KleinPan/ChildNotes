using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Models.Dtos;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

public partial class FeedFormViewModel : ObservableObject
{
    [ObservableProperty] private string _feedType = "bottle";
    [ObservableProperty] private string _amountText = string.Empty;
    [ObservableProperty] private string _leftDurationText = string.Empty;
    [ObservableProperty] private string _rightDurationText = string.Empty;
    [ObservableProperty] private string _timeText = DateTime.Now.ToString("HH:mm");

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
        var dto = new FeedRecordDto { Type = FeedType, Time = TimeText };
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

public partial class DiaperFormViewModel : ObservableObject
{
    [ObservableProperty] private string _diaperType = "wet";
    [ObservableProperty] private string _selectedUrineColor = string.Empty;
    [ObservableProperty] private string _selectedStoolColor = string.Empty;
    [ObservableProperty] private string _selectedConsistency = string.Empty;
    [ObservableProperty] private bool _abnormal;
    [ObservableProperty] private string _timeText = DateTime.Now.ToString("HH:mm");

    public void SelectType(string type) => DiaperType = type;

    public bool Validate(out string error)
    {
        error = string.Empty;
        return true;
    }

    public DiaperRecordDto BuildDto() => new()
    {
        Type = DiaperType,
        UrineColor = SelectedUrineColor,
        Color = SelectedStoolColor,
        Consistency = SelectedConsistency,
        Abnormal = Abnormal,
        Time = TimeText,
    };
}

public partial class SleepFormViewModel : ObservableObject
{
    [ObservableProperty] private string _startTimeText = DateTime.Now.AddHours(-1).ToString("HH:mm");
    [ObservableProperty] private string _endTimeText = DateTime.Now.ToString("HH:mm");
    [ObservableProperty] private string _durationText = string.Empty;

    public bool Validate(out string error)
    {
        error = string.Empty;
        return true;
    }

    public SleepRecordDto BuildDto()
    {
        var dto = new SleepRecordDto { StartTime = StartTimeText, EndTime = EndTimeText, Time = StartTimeText };
        if (TimeSpan.TryParse(StartTimeText, out var s) && TimeSpan.TryParse(EndTimeText, out var e))
        {
            var diff = e - s;
            if (diff.TotalMinutes < 0) diff = diff.Add(TimeSpan.FromDays(1));
            dto.Duration = (int)diff.TotalMinutes;
        }
        return dto;
    }
}

public partial class TemperatureFormViewModel : ObservableObject
{
    [ObservableProperty] private string _temperatureText = string.Empty;
    [ObservableProperty] private bool _isAbnormal;
    [ObservableProperty] private string _note = string.Empty;
    [ObservableProperty] private string _timeText = DateTime.Now.ToString("HH:mm");

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

public partial class GrowthFormViewModel : ObservableObject
{
    [ObservableProperty] private string _heightText = string.Empty;
    [ObservableProperty] private string _weightText = string.Empty;
    [ObservableProperty] private string _note = string.Empty;
    [ObservableProperty] private string _timeText = DateTime.Now.ToString("HH:mm");

    public bool Validate(out string error)
    {
        var hasH = decimal.TryParse(HeightText, out _);
        var hasW = decimal.TryParse(WeightText, out _);
        if (!hasH && !hasW)
        {
            error = "请至少输入身高或体重";
            return false;
        }
        error = string.Empty;
        return true;
    }

    public GrowthRecordDto BuildDto() => new()
    {
        Height = decimal.TryParse(HeightText, out var h) ? h : null,
        Weight = decimal.TryParse(WeightText, out var w) ? w : null,
        Time = TimeText,
    };
}

public partial class SupplementFormViewModel : ObservableObject
{
    [ObservableProperty] private string _suppType = "medicine";
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _dose = string.Empty;
    [ObservableProperty] private string _note = string.Empty;
    [ObservableProperty] private string _timeText = DateTime.Now.ToString("HH:mm");

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

public partial class PumpFormViewModel : ObservableObject
{
    [ObservableProperty] private string _leftDurationText = string.Empty;
    [ObservableProperty] private string _rightDurationText = string.Empty;
    [ObservableProperty] private string _leftAmountText = string.Empty;
    [ObservableProperty] private string _rightAmountText = string.Empty;
    [ObservableProperty] private string _totalAmountText = string.Empty;
    [ObservableProperty] private string _note = string.Empty;
    [ObservableProperty] private string _timeText = DateTime.Now.ToString("HH:mm");

    public bool Validate(out string error)
    {
        var hasTotal = int.TryParse(TotalAmountText, out var total) && total > 0;
        var hasLeft = int.TryParse(LeftAmountText, out var la) && la > 0;
        var hasRight = int.TryParse(RightAmountText, out var ra) && ra > 0;
        if (!hasTotal && !hasLeft && !hasRight)
        {
            error = "请输入吸奶量";
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
            Time = TimeText,
        };
        if (int.TryParse(TotalAmountText, out var total) && total > 0)
            dto.TotalAmount = total;
        else
            dto.TotalAmount = (dto.LeftAmount ?? 0) + (dto.RightAmount ?? 0);
        return dto;
    }
}

public partial class ComplementaryFormViewModel : ObservableObject
{
    [ObservableProperty] private string _foodName = string.Empty;
    [ObservableProperty] private string _selectedTexture = "puree";
    [ObservableProperty] private string _amountText = string.Empty;
    [ObservableProperty] private string _selectedReaction = "none";
    [ObservableProperty] private string _note = string.Empty;
    [ObservableProperty] private string _timeText = DateTime.Now.ToString("HH:mm");

    public void SelectTexture(string t) => SelectedTexture = t;
    public void SelectReaction(string r) => SelectedReaction = r;

    public bool Validate(out string error)
    {
        if (string.IsNullOrWhiteSpace(FoodName))
        {
            error = "请输入食物名称";
            return false;
        }
        error = string.Empty;
        return true;
    }

    public ComplementaryRecordDto BuildDto() => new()
    {
        FoodName = FoodName,
        Texture = SelectedTexture,
        Amount = AmountText,
        Reaction = SelectedReaction,
        Abnormal = SelectedReaction is "allergy" or "vomit" or "diarrhea",
        Note = Note,
        Time = TimeText,
    };
}

public partial class AbnormalFormViewModel : ObservableObject
{
    [ObservableProperty] private string _temperatureText = string.Empty;
    [ObservableProperty] private bool _hasFever;
    [ObservableProperty] private bool _hasDiarrhea;
    [ObservableProperty] private bool _hasVomit;
    [ObservableProperty] private bool _hasCough;
    [ObservableProperty] private bool _hasMedicine;
    [ObservableProperty] private string _note = string.Empty;
    [ObservableProperty] private string _timeText = DateTime.Now.ToString("HH:mm");

    public bool Validate(out string error)
    {
        if (!HasFever && !HasDiarrhea && !HasVomit && !HasCough && !HasMedicine
            && string.IsNullOrWhiteSpace(TemperatureText))
        {
            error = "请至少选择一项异常症状";
            return false;
        }
        error = string.Empty;
        return true;
    }

    public AbnormalRecordDto BuildDto()
    {
        var respiratory = new List<string>();
        if (HasCough) respiratory.Add("cough");
        var diarrhea = new List<string>();
        if (HasDiarrhea) diarrhea.Add("diarrhea");
        return new AbnormalRecordDto
        {
            Temperature = decimal.TryParse(TemperatureText, out var t) ? t : null,
            Respiratory = respiratory,
            Diarrhea = diarrhea,
            Vomit = HasVomit,
            Medicine = HasMedicine,
            Note = Note,
            Time = TimeText,
        };
    }
}

public partial class VaccineFormViewModel : ObservableObject
{
    private readonly RecordService _recordService = ServiceProvider.Instance.RecordService;

    // 时间轴数据
    public ObservableCollection<VaccineTimelineGroup> TimelineGroups { get; } = new();
    public ObservableCollection<CustomVaccine> CustomVaccines { get; } = new();

    // 已选剂次（点击时间轴上某个剂次后高亮）
    [ObservableProperty] private VaccinePlanView? _selectedPlan;
    [ObservableProperty] private string _recordTimeText = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

    // 自定义疫苗表单
    [ObservableProperty] private bool _showCustomVaccineForm;
    [ObservableProperty] private string _customVaccineName = string.Empty;
    [ObservableProperty] private string _customVaccineCategory = "paid"; // free / paid
    [ObservableProperty] private string _customVaccineAgeValue = string.Empty;
    [ObservableProperty] private int _customVaccineAgeUnitIndex = 2;     // 默认月龄
    [ObservableProperty] private string _customVaccineDisease = string.Empty;

    public IReadOnlyList<string> CustomVaccineAgeUnits { get; } = new[] { "日龄", "周龄", "月龄", "周岁" };

    /// <summary>初始化时间轴（在 Open 时调用）</summary>
    public void Load()
    {
        var appState = ServiceProvider.Instance.AppState;
        var birthDate = appState.CurrentBaby?.BirthDate;
        var today = DateTime.Today;
        var vaccineRecords = _recordService.GetByType(RecordType.Vaccine, 1000);

        var plans = VaccineTimelineBuilder.BuildPlans(birthDate, CustomVaccines);
        var views = VaccineTimelineBuilder.BuildPlanViews(plans, vaccineRecords, birthDate, today);
        var groups = VaccineTimelineBuilder.BuildGroups(views);

        TimelineGroups.Clear();
        foreach (var g in groups) TimelineGroups.Add(g);
        SelectedPlan = null;
    }

    /// <summary>点击时间轴上的剂次：选中并展示</summary>
    public void SelectDose(VaccinePlanView plan)
    {
        SelectedPlan = plan;
    }

    /// <summary>标记「已打」某剂次</summary>
    public VaccineRecordDto? MarkDone(VaccinePlanView plan)
    {
        if (plan.Handled) return null;
        var time = string.IsNullOrWhiteSpace(RecordTimeText) ? DateTime.Now.ToString("yyyy-MM-dd HH:mm") : RecordTimeText;
        var dto = BuildDoneDto(plan, time);
        return dto;
    }

    /// <summary>标记「跳过」某剂次</summary>
    public VaccineRecordDto? MarkSkipped(VaccinePlanView plan)
    {
        if (plan.Handled) return null;
        var time = string.IsNullOrWhiteSpace(RecordTimeText) ? DateTime.Now.ToString("yyyy-MM-dd HH:mm") : RecordTimeText;
        var dto = new VaccineRecordDto
        {
            Name = plan.Name,
            VaccineId = string.IsNullOrEmpty(plan.VaccineId) ? null : plan.VaccineId,
            DoseId = string.IsNullOrEmpty(plan.DoseId) ? null : plan.DoseId,
            Category = plan.Category,
            DoseLabel = plan.DoseLabel,
            Disease = plan.Disease,
            Status = "skipped",
            Skipped = true,
            SkippedReason = "手动跳过",
            RecommendedDate = string.IsNullOrEmpty(plan.RecommendedDateText) ? null : plan.RecommendedDateText,
            Custom = plan.VaccineId.Length == 0,
            Note = "手动跳过",
            Time = time,
        };
        return dto;
    }

    private static VaccineRecordDto BuildDoneDto(VaccinePlanView plan, string time)
    {
        return new VaccineRecordDto
        {
            Name = plan.Name,
            VaccineId = string.IsNullOrEmpty(plan.VaccineId) ? null : plan.VaccineId,
            DoseId = string.IsNullOrEmpty(plan.DoseId) ? null : plan.DoseId,
            Category = plan.Category,
            DoseLabel = plan.DoseLabel,
            Disease = plan.Disease,
            Status = "done",
            Skipped = false,
            RecommendedDate = string.IsNullOrEmpty(plan.RecommendedDateText) ? null : plan.RecommendedDateText,
            Custom = plan.VaccineId.Length == 0,
            Time = time,
        };
    }

    /// <summary>添加自定义疫苗到本地集合（不立即持久化，加入时间轴后随「已打」记录一起保存）</summary>
    public (bool Ok, string Error) AddCustomVaccine()
    {
        var name = (CustomVaccineName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(name))
            return (false, "请输入疫苗名称");

        if (!int.TryParse(CustomVaccineAgeValue, out var ageValue) || ageValue <= 0)
            return (false, "请输入接种时间数值");

        if (CustomVaccines.Any(c => c.Name == name))
            return (false, "该疫苗已存在");

        var (dueDays, ageLabel) = BuildCustomDueAndLabel(ageValue, CustomVaccineAgeUnitIndex);
        var cv = new CustomVaccine
        {
            Name = name,
            Category = CustomVaccineCategory == "free" ? "free" : "paid",
            Disease = string.IsNullOrWhiteSpace(CustomVaccineDisease) ? "自定义疫苗" : CustomVaccineDisease.Trim(),
            DoseLabel = "1剂",
            AgeLabel = ageLabel,
            DueDays = dueDays,
        };
        CustomVaccines.Add(cv);

        // 重置表单并刷新时间轴
        CustomVaccineName = string.Empty;
        CustomVaccineDisease = string.Empty;
        CustomVaccineAgeValue = string.Empty;
        ShowCustomVaccineForm = false;
        Load();
        return (true, string.Empty);
    }

    [RelayCommand]
    private void ToggleCustomVaccineForm()
    {
        ShowCustomVaccineForm = !ShowCustomVaccineForm;
    }

    [RelayCommand]
    private void SwitchCustomCategory(string category)
    {
        if (category == "free" || category == "paid")
            CustomVaccineCategory = category;
    }

    private static (int? DueDays, string AgeLabel) BuildCustomDueAndLabel(int value, int unitIndex)
    {
        return unitIndex switch
        {
            0 => (value, $"{value}日龄"),
            1 => (value * 7, $"{value}周龄"),
            2 => (value * 30, $"{value}月龄"),
            3 => (value * 365, $"{value}周岁"),
            _ => (null, $"{value}月龄"),
        };
    }

    // 兼容旧 Save 流程（RecordSheetViewModel.Save 仍会调用 Validate/BuildDto）
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _nextName = string.Empty;
    [ObservableProperty] private string _nextDateText = string.Empty;
    [ObservableProperty] private string _note = string.Empty;
    [ObservableProperty] private string _timeText = DateTime.Now.ToString("HH:mm");

    public bool Validate(out string error)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            error = "请输入疫苗名称";
            return false;
        }
        error = string.Empty;
        return true;
    }

    public VaccineRecordDto BuildDto() => new()
    {
        Name = Name,
        NextName = string.IsNullOrWhiteSpace(NextName) ? null : NextName,
        NextDate = string.IsNullOrWhiteSpace(NextDateText) ? null : NextDateText,
        Note = Note,
        Time = TimeText,
    };
}

public partial class ActivityFormViewModel : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _selectedCategory = "play";
    [ObservableProperty] private string _durationText = string.Empty;
    [ObservableProperty] private string _timeText = DateTime.Now.ToString("HH:mm");

    public void SelectCategory(string c) => SelectedCategory = c;

    public bool Validate(out string error)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            error = "请输入活动名称";
            return false;
        }
        error = string.Empty;
        return true;
    }

    public ActivityRecordDto BuildDto() => new()
    {
        Name = Name,
        Category = SelectedCategory,
        Duration = int.TryParse(DurationText, out var d) ? d : 0,
        Time = TimeText,
    };
}

public partial class MaternalFoodFormViewModel : ObservableObject
{
    [ObservableProperty] private string _selectedMealType = "breakfast";
    [ObservableProperty] private string _foodsText = string.Empty;
    [ObservableProperty] private string _selectedSuspicionLevel = "none";
    [ObservableProperty] private string _note = string.Empty;
    [ObservableProperty] private string _timeText = DateTime.Now.ToString("HH:mm");

    public void SelectMealType(string t) => SelectedMealType = t;
    public void SelectSuspicionLevel(string l) => SelectedSuspicionLevel = l;

    public bool Validate(out string error)
    {
        if (string.IsNullOrWhiteSpace(FoodsText))
        {
            error = "请输入食物";
            return false;
        }
        error = string.Empty;
        return true;
    }

    public MaternalFoodRecordDto BuildDto()
    {
        var foods = FoodsText.Split(new[] { '、', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        return new MaternalFoodRecordDto
        {
            MealType = SelectedMealType,
            Foods = foods,
            SuspicionLevel = SelectedSuspicionLevel,
            Note = Note,
            Time = TimeText,
        };
    }
}
