using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Models.Home;
using ChildNotes.Services;
using ChildNotes.Shared.Constants;
using ChildNotes.Shared.Dtos;

namespace ChildNotes.ViewModels.Home;

/// <summary>
/// 首页疫苗追踪 ViewModel：管理疫苗列表、进度统计、展开/收起交互。
/// 从 HomeViewModel 拆分，职责单一化。
/// </summary>
public partial class VaccineTrackingViewModel : ObservableObject
{
    [ObservableProperty] private ObservableCollection<VaccineItem> _vaccineItems = new();
    [ObservableProperty] private string _vaccineProgressText = "0/0";
    [ObservableProperty] private bool _isVaccineExpanded;

    /// <summary>疫苗列表默认展示条数（对齐小程序折叠态显示 2-3 条的行为）。</summary>
    private const int VaccineDefaultVisibleCount = 3;

    /// <summary>疫苗列表实际渲染数据：展开时返回全部，收起时只返回前 N 条。</summary>
    public IReadOnlyList<VaccineItem> VisibleVaccineItems =>
        IsVaccineExpanded ? VaccineItems : VaccineItems.Take(VaccineDefaultVisibleCount).ToList();

    /// <summary>是否需要展开/收起按钮（总条数 > 默认显示条数时才显示）。</summary>
    public bool NeedsVaccineExpand => VaccineItems.Count > VaccineDefaultVisibleCount;

    /// <summary>
    /// 疫苗列表 ScrollViewer 的 MaxHeight：折叠态约 3 项高度（120，40px/项），展开态约 6 项高度（360，60px/项）。
    /// 配合 VirtualizingStackPanel 实现虚拟化：仅渲染可见区域内的项，57 个剂次不再一次性创建全部 UI 元素。
    /// 展开时通过滚动查看剩余项，而非一次性渲染全部。
    /// </summary>
    public double VaccineListMaxHeight => IsVaccineExpanded ? 360 : 120;

    /// <summary>从快照数据应用疫苗进度（对齐小程序 findNextDosePlans 逻辑：只显示未处理且未到期的下一剂推荐；
    /// 无出生日期或无推荐日的剂次不会被过期过滤跳过，保留为"待安排"候选）。</summary>
    public void ApplyVaccines(List<ChildRecord> vaccineRecords, DateTime? birthDate, DateTime today)
    {
        VaccineItems.Clear();

        // 已处理（已打/已跳过）的疫苗 key 集合
        var handledKeys = new HashSet<string>();
        foreach (var v in vaccineRecords)
        {
            try
            {
                var dto = v.GetPayload<VaccineRecordDto>();
                if (dto is null) continue;
                var key = $"{dto.VaccineId}:{dto.DoseId}";
                if (!string.IsNullOrEmpty(dto.VaccineId) && !string.IsNullOrEmpty(dto.DoseId))
                    handledKeys.Add(key);
                // 也通过名称规范化匹配（兼容历史数据无 vaccineId/doseId 的情况）
                if (!string.IsNullOrEmpty(dto.Name))
                    handledKeys.Add("name:" + NormalizeVaccineName(dto.Name));
            }
            catch { }
        }

        // 收集所有未处理且未到期的候选剂次，按分类分开
        var freeCandidates = new List<(string Key, string Name, string AgeLabel, int? DueDays, string Category)>();
        var paidCandidates = new List<(string Key, string Name, string AgeLabel, int? DueDays, string Category)>();

        foreach (var item in VaccineCatalog.FlattenDoses())
        {
            var (key, name, ageLabel, dueDays, category) = item;

            // 跳过已处理的（已打/已跳过）
            if (handledKeys.Contains(key) || handledKeys.Contains("name:" + NormalizeVaccineName(name)))
                continue;

            // 计算推荐日期，跳过已过期的（recommendedDate < today）
            if (birthDate.HasValue && dueDays.HasValue)
            {
                var recommendedDate = birthDate.Value.AddDays(dueDays.Value).Date;
                if (recommendedDate < today)
                    continue;
            }
            // 无出生日期或无推荐日的仍保留（标记为待安排）

            if (category == "free")
                freeCandidates.Add(item);
            else if (category == "paid")
                paidCandidates.Add(item);
        }

        // 按 DueDays 升序排列（DueDays 越小 = 越早接种），取第一条
        // null DueDays 排在最后（无推荐日期，标记为待安排）
        int CompareByDueDays(
            (string, string, string, int?, string) a,
            (string, string, string, int?, string) b)
        {
            if (a.Item4.HasValue && b.Item4.HasValue)
                return a.Item4.Value.CompareTo(b.Item4.Value);
            if (a.Item4.HasValue) return -1;
            if (b.Item4.HasValue) return 1;
            return 0;
        }

        freeCandidates.Sort(CompareByDueDays);
        paidCandidates.Sort(CompareByDueDays);

        var freePick = freeCandidates.FirstOrDefault();
        var paidPick = paidCandidates.FirstOrDefault();

        if (freePick != default)
            VaccineItems.Add(CreateVaccineItem(freePick.Name, freePick.AgeLabel, freePick.Category, freePick.DueDays, birthDate, today));
        if (paidPick != default)
            VaccineItems.Add(CreateVaccineItem(paidPick.Name, paidPick.AgeLabel, paidPick.Category, paidPick.DueDays, birthDate, today));

        // 统计全部剂次的完成进度
        var totalCount = VaccineCatalog.FlattenDoses().Count;
        var doneCount = 0;
        foreach (var (key, name, _, _, _) in VaccineCatalog.FlattenDoses())
        {
            if (handledKeys.Contains(key) || handledKeys.Contains("name:" + NormalizeVaccineName(name)))
                doneCount++;
        }
        VaccineProgressText = $"{doneCount}/{totalCount}";
        OnPropertyChanged(nameof(NeedsVaccineExpand));
        OnPropertyChanged(nameof(VisibleVaccineItems));
    }

    /// <summary>重置疫苗数据（无宝宝时调用）。</summary>
    public void Reset()
    {
        VaccineItems.Clear();
        VaccineProgressText = "0/0";
        OnPropertyChanged(nameof(NeedsVaccineExpand));
        OnPropertyChanged(nameof(VisibleVaccineItems));
    }

    private static VaccineItem CreateVaccineItem(string name, string ageLabel, string category, int? dueDays, DateTime? birthDate, DateTime today)
    {
        int daysLater;
        if (birthDate.HasValue && dueDays.HasValue)
        {
            var recommendedDate = birthDate.Value.AddDays(dueDays.Value).Date;
            daysLater = (int)(today - recommendedDate).TotalDays;
        }
        else
        {
            daysLater = -1;
        }
        return new VaccineItem(name, category, daysLater, false);
    }

    private static string NormalizeVaccineName(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return System.Text.RegularExpressions.Regex.Replace(s, @"\s+", "")
            .Replace("针", "剂")
            .ToLowerInvariant();
    }

    [RelayCommand]
    private void ToggleVaccinePanel()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        IsVaccineExpanded = !IsVaccineExpanded;
        // OnPropertyChanged(VisibleVaccineItems) 在 OnIsVaccineExpandedChanged 中触发，
        // 同步触发 ItemsControl 重新绑定（后台线程无意义，UI 渲染必须在 UI 线程）。
        // 埋点放在属性变更后，测量"绑定触发→返回"的同步耗时（不含实际渲染，渲染在布局周期异步进行）
        DevLogger.Log("VaccinePerf", $"ToggleVaccinePanel: expanded={IsVaccineExpanded}, items={VaccineItems.Count}, notify_ms={sw.ElapsedMilliseconds}");
    }

    partial void OnIsVaccineExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(VisibleVaccineItems));
        OnPropertyChanged(nameof(VaccineListMaxHeight));
    }
}
