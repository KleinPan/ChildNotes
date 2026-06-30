using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Shared.Constants;
using ChildNotes.Shared.Dtos;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

public partial class VaccineFormViewModel : ObservableObject, IRecordFormViewModel
{
    private readonly RecordService _recordService = ServiceProvider.Instance.RecordService;

    // 时间轴数据
    public ObservableCollection<VaccineTimelineGroup> TimelineGroups { get; } = new();
    public ObservableCollection<CustomVaccine> CustomVaccines { get; } = new();

    // 已选剂次（点击时间轴上某个剂次后高亮）
    [ObservableProperty] private VaccinePlanView? _selectedPlan;
    [ObservableProperty] private string _recordDateText = ServiceProvider.Instance.DateTimeFormatter.FormatDate(DateTime.Now);
    [ObservableProperty] private string _recordTimeText = ServiceProvider.Instance.DateTimeFormatter.FormatTime(DateTime.Now);

    /// <summary>合并日期+时间为 "yyyy-MM-dd HH:mm"。</summary>
    public string RecordDateTimeText
    {
        get
        {
            var d = string.IsNullOrWhiteSpace(RecordDateText) ? ServiceProvider.Instance.DateTimeFormatter.FormatDate(DateTime.Now) : RecordDateText;
            var t = string.IsNullOrWhiteSpace(RecordTimeText) ? ServiceProvider.Instance.DateTimeFormatter.FormatTime(DateTime.Now) : RecordTimeText;
            return $"{d} {t}";
        }
    }

    // 自定义疫苗表单
    [ObservableProperty] private bool _showCustomVaccineForm;
    [ObservableProperty] private string _customVaccineName = string.Empty;
    [ObservableProperty] private string _customVaccineCategory = "paid"; // free / paid
    [ObservableProperty] private string _customVaccineAgeValue = string.Empty;
    [ObservableProperty] private int _customVaccineAgeUnitIndex = 2;     // 默认月龄
    [ObservableProperty] private string _customVaccineDisease = string.Empty;

    public IReadOnlyList<string> CustomVaccineAgeUnits { get; } = new[] { "日龄", "周龄", "月龄", "周岁" };

    /// <summary>初始化时间轴（在 Open 时调用）。异步执行：后台线程构建数据，UI 线程仅做集合同步。</summary>
    public async Task LoadAsync()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var appState = ServiceProvider.Instance.AppState;
        var birthDate = appState.CurrentBaby?.BirthDate;
        var today = DateTime.Today;
        // 拷贝 CustomVaccines 快照，避免后台线程与 UI 集合并发访问
        var customSnapshot = CustomVaccines.ToList();

        // 后台线程执行：DB 读取 + 计划构建 + 状态计算 + 分组
        var groups = await Task.Run(() =>
        {
            var dbSw = System.Diagnostics.Stopwatch.StartNew();
            var vaccineRecords = _recordService.GetByType(RecordType.Vaccine, 1000);
            dbSw.Stop();
            var buildSw = System.Diagnostics.Stopwatch.StartNew();
            var plans = VaccineTimelineBuilder.BuildPlans(birthDate, customSnapshot);
            var views = VaccineTimelineBuilder.BuildPlanViews(plans, vaccineRecords, birthDate, today);
            var result = VaccineTimelineBuilder.BuildGroups(views);
            buildSw.Stop();
            DevLogger.Log("VaccinePerf",
                $"LoadAsync(background) | db={dbSw.ElapsedMilliseconds}ms | build={buildSw.ElapsedMilliseconds}ms | groups={result.Count} | views={views.Count}");
            return result;
        });

        // UI 线程：批量替换集合（Clear + Add 仍会触发通知，但数据已就绪，无需重复构建）
        var uiSw = System.Diagnostics.Stopwatch.StartNew();
        TimelineGroups.Clear();
        foreach (var g in groups) TimelineGroups.Add(g);
        SelectedPlan = null;
        uiSw.Stop();
        sw.Stop();
        DevLogger.Log("VaccinePerf",
            $"LoadAsync(total) | total={sw.ElapsedMilliseconds}ms | ui-sync={uiSw.ElapsedMilliseconds}ms | groups={TimelineGroups.Count}");
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
        var time = RecordDateTimeText;
        var dto = BuildDoneDto(plan, time);
        return dto;
    }

    /// <summary>标记「跳过」某剂次</summary>
    public VaccineRecordDto? MarkSkipped(VaccinePlanView plan)
    {
        if (plan.Handled) return null;
        var time = RecordDateTimeText;
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

    /// <summary>修改已打疫苗记录的接种时间（仅更新时间，保留其他字段）。</summary>
    public VaccineRecordDto? BuildUpdateDoneDto(VaccinePlanView plan)
    {
        if (!plan.Handled || plan.RecordId is null) return null;
        return BuildDoneDto(plan, RecordDateTimeText);
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
    public async Task<(bool Ok, string Error)> AddCustomVaccineAsync()
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
        await LoadAsync();
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
    [ObservableProperty] private string _timeText = ServiceProvider.Instance.DateTimeFormatter.FormatTime(DateTime.Now);

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
