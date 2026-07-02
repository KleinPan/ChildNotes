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
    // TimelineGroups 用 IReadOnlyList 而非 ObservableCollection：
    // LoadAsync 每次整体替换数据（Clear+Add 20 个 group 会触发 20 次 CollectionChanged，
    // 每次都导致 ItemsControl 重建子树，ui-sync 高达 346ms）。
    // 改为属性重赋值只触发 1 次 PropertyChanged，ItemsControl 整体刷新。
    private IReadOnlyList<VaccineTimelineGroup> _timelineGroups = Array.Empty<VaccineTimelineGroup>();
    public IReadOnlyList<VaccineTimelineGroup> TimelineGroups
    {
        get => _timelineGroups;
        private set { _timelineGroups = value; OnPropertyChanged(nameof(TimelineGroups)); }
    }
    public ObservableCollection<CustomVaccine> CustomVaccines { get; } = new();

    // ===== 预加载缓存：Home.Activate 时后台构建，点"补记"时直接用 =====
    private static IReadOnlyList<VaccineTimelineGroup>? s_preloadedGroups;
    private static DateTime? s_preloadedBirthDate;
    private static int s_preloadedCustomCount = -1;

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

    /// <summary>预加载时间轴数据（在 Home.Activate 时后台调用，用户还没点"补记"）。</summary>
    public static async Task PreloadAsync()
    {
        var appState = ServiceProvider.Instance.AppState;
        var birthDate = appState.CurrentBaby?.BirthDate;
        if (birthDate is null) return;

        // 检查是否已有有效缓存（只检查 birthDate，自定义疫苗变化时由 LoadAsync 重新构建）
        if (s_preloadedGroups is not null && s_preloadedBirthDate == birthDate)
        {
            DevLogger.Log("VaccinePerf", "Preload: cache hit, skipped");
            return;
        }

        await Task.Run(() =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var today = DateTime.Today;
            var recordService = ServiceProvider.Instance.RecordService;
            var vaccineRecords = recordService.GetByType(RecordType.Vaccine, 1000);
            // 预加载时自定义疫苗用空列表（用户还未添加自定义疫苗时这是正确的；
            // 若已添加，LoadAsync 会检测到 customCount 不匹配而重建）
            var plans = VaccineTimelineBuilder.BuildPlans(birthDate, Array.Empty<CustomVaccine>());
            var views = VaccineTimelineBuilder.BuildPlanViews(plans, vaccineRecords, birthDate, today);
            var result = VaccineTimelineBuilder.BuildGroups(views);
            sw.Stop();

            s_preloadedGroups = result;
            s_preloadedBirthDate = birthDate;
            s_preloadedCustomCount = 0;  // 预加载时按 0 个自定义疫苗
            DevLogger.Log("VaccinePerf", $"Preload done | total={sw.ElapsedMilliseconds}ms | groups={result.Count}");
        });
    }

    /// <summary>清除预加载缓存（记录疫苗后调用）。</summary>
    public static void InvalidatePreload()
    {
        s_preloadedGroups = null;
        s_preloadedBirthDate = null;
        s_preloadedCustomCount = -1;
    }

    /// <summary>初始化时间轴（在 Open 时调用）。优先使用预加载缓存，否则后台构建。</summary>
    public async Task LoadAsync()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var appState = ServiceProvider.Instance.AppState;
        var birthDate = appState.CurrentBaby?.BirthDate;
        var today = DateTime.Today;
        var customSnapshot = CustomVaccines.ToList();

        IReadOnlyList<VaccineTimelineGroup> groups;

        // 优先使用预加载缓存（命中时几乎无延迟）
        if (s_preloadedGroups is not null &&
            s_preloadedBirthDate == birthDate &&
            s_preloadedCustomCount == customSnapshot.Count)
        {
            groups = s_preloadedGroups;
            DevLogger.Log("VaccinePerf", "LoadAsync: PRELOAD HIT");
        }
        else
        {
            // 缓存未命中，后台构建
            groups = await Task.Run(() =>
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
                    $"LoadAsync(background) | db={dbSw.ElapsedMilliseconds}ms | build={buildSw.ElapsedMilliseconds}ms | groups={result.Count}");
                return result;
            });
        }

        // 渐进式显示：先显示前 4 个 group（~100ms ui-sync），让用户快速看到内容
        var initialCount = Math.Min(4, groups.Count);
        var previewGroups = groups.Take(initialCount).ToList();

        var uiSw = System.Diagnostics.Stopwatch.StartNew();
        TimelineGroups = previewGroups;
        SelectedPlan = null;
        uiSw.Stop();
        DevLogger.Log("VaccinePerf",
            $"LoadAsync(phase1) | total={sw.ElapsedMilliseconds}ms | ui-sync={uiSw.ElapsedMilliseconds}ms | shown={initialCount}/{groups.Count}");

        // 剩余 group 在下一帧追加（不阻塞当前渲染）
        if (groups.Count > initialCount)
        {
            var remaining = groups.Skip(initialCount).ToList();
            _ = Avalonia.Threading.DispatcherTimer.RunOnce(() =>
            {
                var currentList = TimelineGroups.ToList();
                currentList.AddRange(remaining);
                TimelineGroups = currentList;
                sw.Stop();
                DevLogger.Log("VaccinePerf",
                    $"LoadAsync(phase2 done) | total={sw.ElapsedMilliseconds}ms | groups={TimelineGroups.Count}");
            }, TimeSpan.FromMilliseconds(16));  // 下一帧 (~60fps)
        }
        else
        {
            sw.Stop();
            DevLogger.Log("VaccinePerf",
                $"LoadAsync(total) | total={sw.ElapsedMilliseconds}ms | ui-sync={uiSw.ElapsedMilliseconds}ms");
        }
    }

    /// <summary>点击时间轴上的剂次：选中并展示</summary>
    public void SelectDose(VaccinePlanView plan)
    {
        SelectedPlan = plan;
    }

    /// <summary>原地标记「已打」（保存 DB 成功后调用，只更新该卡片 UI，不重建整个时间轴）。</summary>
    public void MarkDoneInline(VaccinePlanView plan, string time, string recordId)
    {
        plan.UpdateForDone(time, recordId);
    }

    /// <summary>原地标记「跳过」。</summary>
    public void MarkSkippedInline(VaccinePlanView plan, string time, string recordId)
    {
        plan.UpdateForSkipped(time, recordId);
    }

    /// <summary>原地取消已打/跳过状态。</summary>
    public void CancelInline(VaccinePlanView plan)
    {
        plan.UpdateForCancel();
    }

    /// <summary>标记「已打」某剂次（构建 DTO，不修改 UI）</summary>
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

    // Vaccine 类型不参与通用 Save 流程（UI 上保存按钮隐藏），Validate 仅满足 IRecordFormViewModel 契约。
    public bool Validate(out string error)
    {
        error = string.Empty;
        return true;
    }
}
