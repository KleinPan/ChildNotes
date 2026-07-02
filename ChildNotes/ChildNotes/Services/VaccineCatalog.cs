using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Shared.Dtos;

namespace ChildNotes.Services;

/// <summary>
/// 疫苗目录（参照原始项目 constants/vaccines.js 简化版）
/// </summary>
public static class VaccineCatalog
{
    public sealed class Dose
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string AgeLabel { get; set; } = string.Empty;
        /// <summary>距出生天数（用于计算推荐接种日期），null 表示按门诊安排</summary>
        public int? DueDays { get; set; }
    }

    public sealed class Vaccine
    {
        public string Id { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty; // free / paid
        public string Name { get; set; } = string.Empty;
        /// <summary>预防疾病</summary>
        public string Disease { get; set; } = string.Empty;
        public List<Dose> Doses { get; set; } = new();
    }

    public static readonly IReadOnlyList<Vaccine> All = new[]
    {
        new Vaccine
        {
            Id = "hepb", Category = "free", Name = "乙肝疫苗",
            Doses =
            [
                new Dose { Id = "dose1", Label = "第1剂", AgeLabel = "出生时", DueDays = 0 },
                new Dose { Id = "dose2", Label = "第2剂", AgeLabel = "1月龄", DueDays = 30 },
                new Dose { Id = "dose3", Label = "第3剂", AgeLabel = "6月龄", DueDays = 180 },
            ],
        },
        new Vaccine
        {
            Id = "bcg", Category = "free", Name = "卡介苗",
            Doses = [new Dose { Id = "dose1", Label = "1剂", AgeLabel = "出生时", DueDays = 0 }],
        },
        new Vaccine
        {
            Id = "ipv", Category = "free", Name = "脊灰灭活疫苗(IPV)",
            Doses =
            [
                new Dose { Id = "dose1", Label = "第1剂", AgeLabel = "2月龄", DueDays = 60 },
                new Dose { Id = "dose2", Label = "第2剂", AgeLabel = "3月龄", DueDays = 90 },
            ],
        },
        new Vaccine
        {
            Id = "bopv", Category = "free", Name = "脊灰减毒活疫苗(bOPV)",
            Doses =
            [
                new Dose { Id = "dose1", Label = "第1剂", AgeLabel = "4月龄", DueDays = 120 },
                new Dose { Id = "dose2", Label = "第2剂", AgeLabel = "4周岁", DueDays = 1460 },
            ],
        },
        new Vaccine
        {
            Id = "dtap", Category = "free", Name = "百白破疫苗", Disease = "白喉、破伤风、百日咳",
            Doses =
            [
                new Dose { Id = "dose1", Label = "第1剂", AgeLabel = "3月龄", DueDays = 90 },
                new Dose { Id = "dose2", Label = "第2剂", AgeLabel = "4月龄", DueDays = 120 },
                new Dose { Id = "dose3", Label = "第3剂", AgeLabel = "5月龄", DueDays = 150 },
                new Dose { Id = "dose4", Label = "加强1剂", AgeLabel = "18月龄", DueDays = 540 },
                new Dose { Id = "dose5", Label = "第5剂加强", AgeLabel = "6周岁", DueDays = 2190 },
            ],
        },
        new Vaccine
        {
            Id = "men_a", Category = "free", Name = "A群流脑多糖疫苗", Disease = "A群脑膜炎球菌",
            Doses =
            [
                new Dose { Id = "dose1", Label = "第1剂", AgeLabel = "6月龄", DueDays = 180 },
                new Dose { Id = "dose2", Label = "第2剂", AgeLabel = "9月龄", DueDays = 270 },
            ],
        },
        new Vaccine
        {
            Id = "mmr", Category = "free", Name = "麻腮风疫苗",
            Doses =
            [
                new Dose { Id = "dose1", Label = "第1剂", AgeLabel = "8月龄", DueDays = 240 },
                new Dose { Id = "dose2", Label = "第2剂", AgeLabel = "18月龄", DueDays = 540 },
            ],
        },
        new Vaccine
        {
            Id = "je_live", Category = "free", Name = "乙脑减毒活疫苗",
            Doses =
            [
                new Dose { Id = "dose1", Label = "第1剂", AgeLabel = "8月龄", DueDays = 240 },
                new Dose { Id = "dose2", Label = "第2剂", AgeLabel = "2周岁", DueDays = 730 },
            ],
        },
        new Vaccine
        {
            Id = "hepa_live", Category = "free", Name = "甲肝减毒活疫苗", Disease = "甲型肝炎",
            Doses = [new Dose { Id = "dose1", Label = "1剂", AgeLabel = "18月龄", DueDays = 540 }],
        },
        new Vaccine
        {
            Id = "men_ac", Category = "free", Name = "A群C群流脑多糖疫苗",
            Doses =
            [
                new Dose { Id = "dose1", Label = "第1剂", AgeLabel = "3周岁", DueDays = 1095 },
                new Dose { Id = "dose2", Label = "第2剂", AgeLabel = "6周岁", DueDays = 2190 },
            ],
        },
        new Vaccine
        {
            Id = "pcv13", Category = "paid", Name = "13价肺炎球菌结合疫苗",
            Doses =
            [
                new Dose { Id = "dose1", Label = "第1剂", AgeLabel = "2月龄", DueDays = 60 },
                new Dose { Id = "dose2", Label = "第2剂", AgeLabel = "4月龄", DueDays = 120 },
                new Dose { Id = "dose3", Label = "第3剂", AgeLabel = "6月龄", DueDays = 180 },
                new Dose { Id = "dose4", Label = "加强1剂", AgeLabel = "12-15月龄", DueDays = 365 },
            ],
        },
        new Vaccine
        {
            Id = "rota5", Category = "paid", Name = "五价轮状病毒疫苗", Disease = "轮状病毒腹泻",
            Doses =
            [
                new Dose { Id = "dose1", Label = "第1剂", AgeLabel = "6-12周龄", DueDays = 42 },
                new Dose { Id = "dose2", Label = "第2剂", AgeLabel = "10-22周龄", DueDays = 70 },
                new Dose { Id = "dose3", Label = "第3剂", AgeLabel = "14-32周龄", DueDays = 98 },
            ],
        },
        new Vaccine
        {
            Id = "hib", Category = "paid", Name = "b型流感嗜血杆菌(Hib)疫苗",
            Doses =
            [
                new Dose { Id = "dose1", Label = "第1剂", AgeLabel = "2月龄", DueDays = 60 },
                new Dose { Id = "dose2", Label = "第2剂", AgeLabel = "3月龄", DueDays = 90 },
                new Dose { Id = "dose3", Label = "第3剂", AgeLabel = "4月龄", DueDays = 120 },
                new Dose { Id = "dose4", Label = "加强1剂", AgeLabel = "18月龄", DueDays = 540 },
            ],
        },
        new Vaccine
        {
            Id = "varicella", Category = "paid", Name = "水痘疫苗",
            Doses =
            [
                new Dose { Id = "dose1", Label = "第1剂", AgeLabel = "12月龄", DueDays = 365 },
                new Dose { Id = "dose2", Label = "第2剂", AgeLabel = "4-6岁", DueDays = 1460 },
            ],
        },
        new Vaccine
        {
            Id = "flu", Category = "paid", Name = "流感疫苗", Disease = "流行性感冒",
            Doses =
            [
                new Dose { Id = "dose1", Label = "首次第1剂", AgeLabel = "满6月龄", DueDays = 180 },
                new Dose { Id = "dose2", Label = "首次第2剂", AgeLabel = "间隔4周", DueDays = 210 },
            ],
        },
        new Vaccine
        {
            Id = "ev71", Category = "paid", Name = "肠道病毒71型(EV71)疫苗",
            Doses =
            [
                new Dose { Id = "dose1", Label = "第1剂", AgeLabel = "6月龄", DueDays = 180 },
                new Dose { Id = "dose2", Label = "第2剂", AgeLabel = "7月龄", DueDays = 210 },
            ],
        },
        new Vaccine
        {
            Id = "pentavalent", Category = "paid", Name = "五联疫苗",
            Doses =
            [
                new Dose { Id = "dose1", Label = "第1剂", AgeLabel = "2月龄", DueDays = 60 },
                new Dose { Id = "dose2", Label = "第2剂", AgeLabel = "3月龄", DueDays = 90 },
                new Dose { Id = "dose3", Label = "第3剂", AgeLabel = "4月龄", DueDays = 120 },
                new Dose { Id = "dose4", Label = "加强1剂", AgeLabel = "18月龄", DueDays = 540 },
            ],
        },
        new Vaccine
        {
            Id = "hepa_inact", Category = "paid", Name = "甲肝灭活疫苗",
            Doses =
            [
                new Dose { Id = "dose1", Label = "第1剂", AgeLabel = "18月龄", DueDays = 540 },
                new Dose { Id = "dose2", Label = "第2剂", AgeLabel = "24月龄", DueDays = 730 },
            ],
        },
        new Vaccine
        {
            Id = "je_inact", Category = "paid", Name = "乙脑灭活疫苗", Disease = "流行性乙型脑炎",
            Doses =
            [
                new Dose { Id = "dose1", Label = "第1剂", AgeLabel = "8月龄", DueDays = 240 },
                new Dose { Id = "dose2", Label = "第2剂", AgeLabel = "间隔7-10天", DueDays = 250 },
                new Dose { Id = "dose3", Label = "第3剂", AgeLabel = "2周岁", DueDays = 730 },
                new Dose { Id = "dose4", Label = "第4剂", AgeLabel = "6周岁", DueDays = 2190 },
            ],
        },
        new Vaccine
        {
            Id = "men_acyw135", Category = "paid", Name = "ACYW135群流脑多糖疫苗", Disease = "A、C、Y、W135群流脑",
            Doses = [new Dose { Id = "dose1", Label = "第1剂", AgeLabel = "2岁以上", DueDays = 730 }],
        },
        new Vaccine
        {
            Id = "cholera", Category = "paid", Name = "霍乱疫苗", Disease = "霍乱及ETEC腹泻",
            Doses =
            [
                new Dose { Id = "dose1", Label = "第1剂", AgeLabel = "2岁以上", DueDays = 730 },
                new Dose { Id = "dose2", Label = "第2剂", AgeLabel = "间隔7天", DueDays = 737 },
                new Dose { Id = "dose3", Label = "第3剂", AgeLabel = "间隔28天", DueDays = 758 },
            ],
        },
        new Vaccine
        {
            Id = "men_ac_conj", Category = "paid", Name = "A+C群流脑结合疫苗", Disease = "A群、C群流行性脑脊髓膜炎",
            Doses =
            [
                new Dose { Id = "dose1", Label = "第1剂", AgeLabel = "6月龄", DueDays = 180 },
                new Dose { Id = "dose2", Label = "第2剂", AgeLabel = "9月龄", DueDays = 270 },
                new Dose { Id = "dose3", Label = "加强1剂", AgeLabel = "3周岁", DueDays = 1095 },
                new Dose { Id = "dose4", Label = "加强2剂", AgeLabel = "6周岁", DueDays = 2190 },
            ],
        },
    };

    /// <summary>
    /// 缓存的剂次展开列表（含分类信息）。疫苗目录是静态数据，
    /// 结果不可变，缓存后避免每次 RefreshAsync 重新枚举 52 项 + 字符串拼接。
    /// </summary>
    private static readonly List<(string Key, string Name, string AgeLabel, int? DueDays, string Category)> FlattenedDosesCache =
        BuildFlattenedDosesCache();

    private static List<(string Key, string Name, string AgeLabel, int? DueDays, string Category)> BuildFlattenedDosesCache()
    {
        var list = new List<(string, string, string, int?, string)>(64);
        foreach (var v in All)
        {
            foreach (var d in v.Doses)
            {
                var name = string.IsNullOrEmpty(d.Label) ? v.Name : $"{v.Name} {d.Label}";
                list.Add(($"{v.Id}:{d.Id}", name, d.AgeLabel, d.DueDays, v.Category));
            }
        }
        return list;
    }

    /// <summary>
    /// 展开所有剂次，返回 (Key, 疫苗名+剂次, 月龄标签, 距出生天数, 分类) 列表。
    /// Key 格式为 "vaccineId:doseId"，Category 为 "free"/"paid"。
    /// 结果在首次调用时构建并缓存，后续调用直接返回缓存引用（疫苗目录是静态数据）。
    /// </summary>
    public static IReadOnlyList<(string Key, string Name, string AgeLabel, int? DueDays, string Category)> FlattenDoses() => FlattenedDosesCache;

    /// <summary>自费疫苗替代免费疫苗的映射：key=自费剂次key，value=被替代的免费剂次key列表</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Replacements = new Dictionary<string, IReadOnlyList<string>>
    {
        ["pentavalent:dose1"] = ["ipv:dose1", "dtap:dose1", "hib:dose1"],
        ["pentavalent:dose2"] = ["ipv:dose2", "dtap:dose2", "hib:dose2"],
        ["pentavalent:dose3"] = ["dtap:dose3", "hib:dose3"],
        ["pentavalent:dose4"] = ["dtap:dose4", "hib:dose4"],
        ["hepa_inact:dose1"] = ["hepa_live:dose1"],
        ["hepa_inact:dose2"] = [],
        ["je_inact:dose1"] = ["je_live:dose1"],
        ["je_inact:dose3"] = ["je_live:dose2"],
        ["men_ac:dose1"] = ["men_a:dose1"],
        ["men_ac:dose2"] = ["men_a:dose2"],
        ["men_acyw135:dose1"] = ["men_ac:dose1"],
        ["men_ac_conj:dose1"] = ["men_a:dose1"],
        ["men_ac_conj:dose2"] = ["men_a:dose2"],
        ["men_ac_conj:dose3"] = ["men_ac:dose1"],
        ["men_ac_conj:dose4"] = ["men_ac:dose2"],
    };

    /// <summary>根据 VaccineId+DoseId 查找疫苗与剂次</summary>
    public static (Vaccine Vaccine, Dose Dose)? FindDose(string vaccineId, string doseId)
    {
        foreach (var v in All)
        {
            if (v.Id != vaccineId) continue;
            foreach (var d in v.Doses)
            {
                if (d.Id == doseId) return (v, d);
            }
        }
        return null;
    }
}

/// <summary>疫苗剂次计划（用于时间轴展示与状态计算）</summary>
public class VaccinePlan
{
    public string Key { get; set; } = string.Empty;       // vaccineId:doseId
    public string VaccineId { get; set; } = string.Empty;
    public string DoseId { get; set; } = string.Empty;
    public string VaccineName { get; set; } = string.Empty;
    public string Disease { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;  // free / paid
    public string DoseLabel { get; set; } = string.Empty;
    public string AgeLabel { get; set; } = string.Empty;
    public int? DueDays { get; set; }
    public DateTime? RecommendedDate { get; set; }
    public string RecommendedDateText { get; set; } = string.Empty;
    /// <summary>剂次完整名称：疫苗名 + 剂次标签</summary>
    public string Name => string.IsNullOrEmpty(DoseLabel) ? VaccineName : $"{VaccineName} {DoseLabel}";
    /// <summary>距出生天数的排序值（无 DueDays 时按 9999 排末尾）</summary>
    public int DueSortValue => DueDays ?? 9999;
}

/// <summary>剂次状态枚举</summary>
public static class VaccineDoseStatus
{
    public const string Done = "done";
    public const string Skipped = "skipped";
    public const string Replaced = "replaced";
    public const string Overdue = "overdue";
    public const string Due = "due";
    public const string Soon = "soon";
    public const string Pending = "pending";
}

/// <summary>未处理剂次的状态计算（按推荐日期与今天的天数差）。
/// 供 BuildPlanViews 初始化和 UpdateForCancel 恢复状态复用，确保取消后状态与初次构建一致。</summary>
public static class VaccineStatusCalculator
{
    /// <summary>根据推荐日期计算未处理剂次的状态文本与样式类。无推荐日期时返回 Pending。</summary>
    public static (string Status, string StatusText, string StatusClass) ComputePending(DateTime? recommendedDate, DateTime today)
    {
        if (!recommendedDate.HasValue)
            return (VaccineDoseStatus.Pending, "待安排", "pending");

        var diff = (recommendedDate.Value - today).Days;
        if (diff < 0) return (VaccineDoseStatus.Overdue, $"已过期{-diff}天", "overdue");
        if (diff == 0) return (VaccineDoseStatus.Due, "今天可打", "due");
        if (diff <= 30) return (VaccineDoseStatus.Soon, $"{diff}天后", "soon");
        return (VaccineDoseStatus.Pending, "待安排", "pending");
    }
}

/// <summary>时间轴分组（一个年龄段一行，含左免费/右自费两列）</summary>
public sealed class VaccineTimelineGroup
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;          // 如 "1月龄"
    public string DateText { get; set; } = string.Empty;       // 推荐日期
    public List<VaccinePlanView> FreeDoses { get; set; } = new();
    public List<VaccinePlanView> PaidDoses { get; set; } = new();
    public bool HasFree => FreeDoses.Count > 0;
    public bool HasPaid => PaidDoses.Count > 0;
    public int HandledCount => FreeDoses.Count(d => d.Status is VaccineDoseStatus.Done or VaccineDoseStatus.Skipped or VaccineDoseStatus.Replaced)
                             + PaidDoses.Count(d => d.Status is VaccineDoseStatus.Done or VaccineDoseStatus.Skipped or VaccineDoseStatus.Replaced);
    public int TotalCount => FreeDoses.Count + PaidDoses.Count;
}

/// <summary>带状态的剂次计划（时间轴渲染用）
/// 实现 INPC 以支持原地更新单个卡片状态（避免全量重建时间轴导致抖动）。</summary>
public sealed class VaccinePlanView : VaccinePlan, System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

    private string _status = VaccineDoseStatus.Pending;
    public string Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged(nameof(Status));
                UpdateBoolProps();
            }
        }
    }

    /// <summary>
    /// 构造时调用 UpdateBoolProps 让 IsPending 等属性与默认 _status=Pending 一致。
    /// 否则通过对象初始化器 new VaccinePlanView { Status = "pending" } 时，
    /// 因新值等于默认值，setter 中 if (_status != value) 为 false，UpdateBoolProps 不会执行，
    /// 导致 IsPending 永远为 false（默认 bool 值），与 StatusClass 契约不一致。
    /// </summary>
    public VaccinePlanView() => UpdateBoolProps();

    private string _statusText = string.Empty;
    public string StatusText { get => _statusText; set { if (_statusText != value) { _statusText = value; OnPropertyChanged(nameof(StatusText)); } } }

    private string _statusClass = string.Empty;
    public string StatusClass { get => _statusClass; set { if (_statusClass != value) { _statusClass = value; OnPropertyChanged(nameof(StatusClass)); } } }

    /// <summary>
    /// 预计算的状态 bool 属性，供 AXAML Classes.xxx 绑定直接使用，
    /// 替代原先通过 EqualsConverter 对 StatusClass 字符串求值的方式。
    /// 每个剂次卡片减少 7 次转换器实例调用（ToString + 字符串比较）。
    /// </summary>
    public bool IsDone { get; private set; }
    public bool IsSkipped { get; private set; }
    public bool IsReplaced { get; private set; }
    public bool IsOverdue { get; private set; }
    public bool IsDue { get; private set; }
    public bool IsSoon { get; private set; }
    public bool IsPending { get; private set; }

    private void UpdateBoolProps()
    {
        var oldDone = IsDone; var oldSkipped = IsSkipped; var oldReplaced = IsReplaced;
        var oldOverdue = IsOverdue; var oldDue = IsDue; var oldSoon = IsSoon; var oldPending = IsPending;

        IsDone = Status == VaccineDoseStatus.Done;
        IsSkipped = Status == VaccineDoseStatus.Skipped;
        IsReplaced = Status == VaccineDoseStatus.Replaced;
        IsOverdue = Status == VaccineDoseStatus.Overdue;
        IsDue = Status == VaccineDoseStatus.Due;
        IsSoon = Status == VaccineDoseStatus.Soon;
        IsPending = Status == VaccineDoseStatus.Pending;

        // 只通知实际变化的属性，减少不必要的绑定刷新
        if (oldDone != IsDone) OnPropertyChanged(nameof(IsDone));
        if (oldSkipped != IsSkipped) OnPropertyChanged(nameof(IsSkipped));
        if (oldReplaced != IsReplaced) OnPropertyChanged(nameof(IsReplaced));
        if (oldOverdue != IsOverdue) OnPropertyChanged(nameof(IsOverdue));
        if (oldDue != IsDue) OnPropertyChanged(nameof(IsDue));
        if (oldSoon != IsSoon) OnPropertyChanged(nameof(IsSoon));
        if (oldPending != IsPending) OnPropertyChanged(nameof(IsPending));

        // 联动 NotHandled / Handled
        var newHandled = IsDone || IsSkipped || IsReplaced;
        if (Handled != newHandled)
        {
            Handled = newHandled;
            OnPropertyChanged(nameof(Handled));
            OnPropertyChanged(nameof(NotHandled));
        }
    }

    private bool _handled;
    public bool Handled { get => _handled; set { if (_handled != value) { _handled = value; OnPropertyChanged(nameof(Handled)); OnPropertyChanged(nameof(NotHandled)); } } }
    public bool NotHandled => !Handled;

    private string? _doneTime;
    public string? DoneTime { get => _doneTime; set { if (_doneTime != value) { _doneTime = value; OnPropertyChanged(nameof(DoneTime)); } } }

    private string? _skippedTime;
    public string? SkippedTime { get => _skippedTime; set { if (_skippedTime != value) { _skippedTime = value; OnPropertyChanged(nameof(SkippedTime)); } } }

    private string? _replacedByName;
    public string? ReplacedByName { get => _replacedByName; set { if (_replacedByName != value) { _replacedByName = value; OnPropertyChanged(nameof(ReplacedByName)); } } }

    private long? _recordId;
    /// <summary>已处理剂次对应的数据库记录 ID（用于修改/删除操作），未处理时为 null。</summary>
    public long? RecordId { get => _recordId; set { if (_recordId != value) { _recordId = value; OnPropertyChanged(nameof(RecordId)); } } }

    /// <summary>原地更新为"已打"状态（避免 LoadAsync 全量重建导致时间轴抖动）。</summary>
    public void UpdateForDone(string time, long recordId)
    {
        Status = VaccineDoseStatus.Done;
        StatusText = "已打";
        StatusClass = "done";
        DoneTime = ServiceProvider.Instance.DateTimeFormatter.FormatDateTime(DateTime.Parse(time));
        RecordId = recordId;
    }

    /// <summary>原地更新为"跳过"状态。</summary>
    public void UpdateForSkipped(string time, long recordId)
    {
        Status = VaccineDoseStatus.Skipped;
        StatusText = "已跳过";
        StatusClass = "skipped";
        SkippedTime = ServiceProvider.Instance.DateTimeFormatter.FormatDateTime(DateTime.Parse(time));
        RecordId = recordId;
    }

    /// <summary>原地取消已打/跳过状态，按推荐日期重新计算恢复为正确的未处理状态
    /// （Overdue/Due/Soon/Pending），与 BuildPlanViews 初次构建逻辑一致。</summary>
    public void UpdateForCancel()
    {
        var (st, stText, stClass) = VaccineStatusCalculator.ComputePending(RecommendedDate, DateTime.Today);
        Status = st;
        StatusText = stText;
        StatusClass = stClass;
        DoneTime = null;
        SkippedTime = null;
        RecordId = null;
    }
}

/// <summary>自定义疫苗（用户手动添加）</summary>
public sealed class CustomVaccine
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "paid";
    public string Disease { get; set; } = "自定义疫苗";
    public string DoseLabel { get; set; } = "1剂";
    public string AgeLabel { get; set; } = string.Empty;
    public int? DueDays { get; set; }
}

/// <summary>疫苗时间轴构建器（对齐小程序 buildTimelineGroups / buildDosePlans 逻辑）</summary>
public static class VaccineTimelineBuilder
{
    // ===== 缓存：BuildPlans 结果（birthDate + customVaccines 不变时复用）=====
    private static DateTime? s_cachedBirthDate;
    private static int s_cachedCustomCount = -1;
    private static List<VaccinePlan>? s_cachedPlans;

    // ===== 缓存：预编译正则（避免每次 NormalizeName 都 new Regex）=====
    private static readonly System.Text.RegularExpressions.Regex s_normalizeRegex = new(@"\s+", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>清除 BuildPlans 缓存（在记录/修改疫苗后调用，强制下次重建）。</summary>
    public static void InvalidateCache()
    {
        s_cachedPlans = null;
        s_cachedBirthDate = null;
        s_cachedCustomCount = -1;
    }

    /// <summary>构建所有剂次计划（含自定义疫苗），不附带状态。带缓存：birthDate 和自定义疫苗数量不变时直接返回缓存。</summary>
    public static List<VaccinePlan> BuildPlans(DateTime? birthDate, IReadOnlyList<CustomVaccine> customVaccines)
    {
        // 命中缓存：birthDate 相同且自定义疫苗数量未变
        if (s_cachedPlans is not null &&
            s_cachedBirthDate == birthDate &&
            s_cachedCustomCount == customVaccines.Count)
        {
            DevLogger.Log("VaccinePerf", "BuildPlans CACHE HIT");
            return s_cachedPlans;
        }

        var plans = new List<VaccinePlan>();
        foreach (var v in VaccineCatalog.All)
        {
            foreach (var d in v.Doses)
            {
                var plan = new VaccinePlan
                {
                    Key = $"{v.Id}:{d.Id}",
                    VaccineId = v.Id,
                    DoseId = d.Id,
                    VaccineName = v.Name,
                    Disease = v.Disease,
                    Category = v.Category,
                    DoseLabel = d.Label,
                    AgeLabel = d.AgeLabel,
                    DueDays = d.DueDays,
                };
                if (birthDate.HasValue && d.DueDays.HasValue)
                {
                    plan.RecommendedDate = birthDate.Value.AddDays(d.DueDays.Value).Date;
                    plan.RecommendedDateText = ServiceProvider.Instance.DateTimeFormatter.FormatDate(plan.RecommendedDate.Value);
                }
                plans.Add(plan);
            }
        }
        // 追加自定义疫苗
        foreach (var cv in customVaccines)
        {
            var plan = new VaccinePlan
            {
                Key = $"custom:{cv.Name}",
                VaccineId = string.Empty,
                DoseId = string.Empty,
                VaccineName = cv.Name,
                Disease = cv.Disease,
                Category = cv.Category,
                DoseLabel = cv.DoseLabel,
                AgeLabel = cv.AgeLabel,
                DueDays = cv.DueDays,
            };
            if (birthDate.HasValue && cv.DueDays.HasValue)
            {
                plan.RecommendedDate = birthDate.Value.AddDays(cv.DueDays.Value).Date;
                plan.RecommendedDateText = ServiceProvider.Instance.DateTimeFormatter.FormatDate(plan.RecommendedDate.Value);
            }
            plans.Add(plan);
        }

        // 写入缓存
        s_cachedBirthDate = birthDate;
        s_cachedCustomCount = customVaccines.Count;
        s_cachedPlans = plans;

        return plans;
    }

    /// <summary>根据历史记录计算每个剂次的状态，返回带状态的视图</summary>
    public static List<VaccinePlanView> BuildPlanViews(
        IReadOnlyList<VaccinePlan> plans,
        IReadOnlyList<ChildRecord> vaccineRecords,
        DateTime? birthDate,
        DateTime today)
    {
        var views = new List<VaccinePlanView>(plans.Count);
        var doneMap = new Dictionary<string, ChildRecord>();   // key -> record
        var skippedMap = new Dictionary<string, ChildRecord>();

        foreach (var rec in vaccineRecords)
        {
            VaccineRecordDto? dto = null;
            try { dto = rec.GetPayload<VaccineRecordDto>(); } catch { }
            if (dto is null) continue;

            var key = BuildRecordKey(dto, plans);
            if (string.IsNullOrEmpty(key)) continue;

            if (dto.Skipped == true || dto.Status == "skipped")
                skippedMap.TryAdd(key, rec);
            else
                doneMap.TryAdd(key, rec);
        }

        // 被替代的剂次集合
        var replacedSet = new HashSet<string>();
        foreach (var kv in VaccineCatalog.Replacements)
        {
            if (doneMap.ContainsKey(kv.Key) || skippedMap.ContainsKey(kv.Key))
            {
                foreach (var replacedKey in kv.Value) replacedSet.Add(replacedKey);
            }
        }

        foreach (var p in plans)
        {
            var view = new VaccinePlanView
            {
                Key = p.Key,
                VaccineId = p.VaccineId,
                DoseId = p.DoseId,
                VaccineName = p.VaccineName,
                Disease = p.Disease,
                Category = p.Category,
                DoseLabel = p.DoseLabel,
                AgeLabel = p.AgeLabel,
                DueDays = p.DueDays,
                RecommendedDate = p.RecommendedDate,
                RecommendedDateText = p.RecommendedDateText,
            };

            if (doneMap.TryGetValue(p.Key, out var doneRec))
            {
                view.Status = VaccineDoseStatus.Done;
                view.StatusText = "已打";
                view.StatusClass = "done";
                view.Handled = true;
                view.DoneTime = FormatRecordTime(doneRec, birthDate);
                view.RecordId = doneRec.Id;
            }
            else if (skippedMap.TryGetValue(p.Key, out var skippedRec))
            {
                view.Status = VaccineDoseStatus.Skipped;
                view.StatusText = "已跳过";
                view.StatusClass = "skipped";
                view.Handled = true;
                view.SkippedTime = FormatRecordTime(skippedRec, birthDate);
                view.RecordId = skippedRec.Id;
            }
            else if (replacedSet.Contains(p.Key))
            {
                view.Status = VaccineDoseStatus.Replaced;
                view.StatusText = "已替代";
                view.StatusClass = "replaced";
                view.Handled = true;
                view.ReplacedByName = FindReplacedByName(p.Key);
            }
            else
            {
                // 未处理：按推荐日期计算（复用 VaccineStatusCalculator，与 UpdateForCancel 恢复状态一致）
                var (st, stText, stClass) = VaccineStatusCalculator.ComputePending(p.RecommendedDate, today);
                view.Status = st;
                view.StatusText = stText;
                view.StatusClass = stClass;
            }
            views.Add(view);
        }
        return views;
    }

    /// <summary>构建时间轴分组（按 DueSortValue 分组，每组分 free/paid 两列）</summary>
    public static List<VaccineTimelineGroup> BuildGroups(List<VaccinePlanView> views)
    {
        var groups = new List<VaccineTimelineGroup>();
        var grouped = views.GroupBy(v => v.DueSortValue).OrderBy(g => g.Key);
        foreach (var g in grouped)
        {
            var first = g.First();
            var group = new VaccineTimelineGroup
            {
                Key = $"due-{g.Key}",
                Name = first.AgeLabel,
                DateText = first.RecommendedDateText,
            };
            foreach (var v in g)
            {
                if (v.Category == "free") group.FreeDoses.Add(v);
                else group.PaidDoses.Add(v);
            }
            // 按名称排序，保证稳定
            group.FreeDoses.Sort((a, b) => string.Compare(a.VaccineName, b.VaccineName, StringComparison.Ordinal));
            group.PaidDoses.Sort((a, b) => string.Compare(a.VaccineName, b.VaccineName, StringComparison.Ordinal));
            groups.Add(group);
        }
        return groups;
    }

    /// <summary>根据 DTO 构造剂次 key（先精确匹配，缺失时按名称兜底）</summary>
    private static string BuildRecordKey(VaccineRecordDto dto, IReadOnlyList<VaccinePlan> plans)
    {
        if (!string.IsNullOrEmpty(dto.VaccineId) && !string.IsNullOrEmpty(dto.DoseId))
            return $"{dto.VaccineId}:{dto.DoseId}";

        // 按名称兜底（兼容旧记录）
        if (string.IsNullOrEmpty(dto.Name)) return string.Empty;
        var normalized = NormalizeName(dto.Name);
        foreach (var p in plans)
        {
            if (NormalizeName(p.Name) == normalized) return p.Key;
        }
        return string.Empty;
    }

    private static string NormalizeName(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s_normalizeRegex.Replace(s, "")
            .Replace("针", "剂")
            .ToLowerInvariant();
    }

    private static string? FormatRecordTime(ChildRecord rec, DateTime? birthDate)
    {
        if (rec.RecordTime != default) return ServiceProvider.Instance.DateTimeFormatter.FormatDateTime(rec.RecordTime);
        return null;
    }

    private static string? FindReplacedByName(string key)
    {
        foreach (var kv in VaccineCatalog.Replacements)
        {
            if (kv.Value.Contains(key))
            {
                var parts = kv.Key.Split(':');
                if (parts.Length == 2)
                {
                    var found = VaccineCatalog.FindDose(parts[0], parts[1]);
                    if (found.HasValue) return found.Value.Vaccine.Name;
                }
            }
        }
        return null;
    }
}
