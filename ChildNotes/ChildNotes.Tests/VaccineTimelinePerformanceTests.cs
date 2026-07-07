using System.Diagnostics;
using ChildNotes.Data;
using ChildNotes.Data.Repositories;
using ChildNotes.Infrastructure;
using ChildNotes.Services;
using ChildNotes.Shared.Constants;
using SQLitePCL;

namespace ChildNotes.Tests;

/// <summary>
/// 疫苗时间轴构建性能基准测试。
///
/// 测试目标：量化 VaccineTimelineBuilder 的数据构建耗时，作为弹出面板优化前后的对比基准。
/// 该测试不依赖 UI 线程，仅测量后台线程执行的数据构建逻辑（DB 读取 + 计划构建 + 状态计算 + 分组）。
///
/// 运行方式：dotnet test ChildNotes.Tests --filter "FullyQualifiedName~VaccineTimelinePerformance"
/// </summary>
public class VaccineTimelinePerformanceTests : IDisposable
{
    private readonly DbConnectionFactory _factory;
    private readonly RecordRepository _recordRepo;
    private readonly RecordService _recordService;
    private readonly AppState _state;

    public VaccineTimelinePerformanceTests()
    {
        Batteries_V2.Init();
        var tmpDb = Path.Combine(Path.GetTempPath(), $"cn_perf_{Guid.NewGuid():N}.db");
        _factory = new DbConnectionFactory(tmpDb);
        DbInitializer.Initialize(_factory);
        _recordRepo = new RecordRepository(_factory);
        _state = new AppState();
        _recordService = new RecordService(_recordRepo, _state);
    }

    public void Dispose() { }

    /// <summary>
    /// 基准测试：空数据库（无历史记录）下构建时间轴的耗时。
    /// 这是首次打开疫苗面板的最坏情况（所有剂次都需要计算状态）。
    /// 注：使用 null birthDate 避免 ServiceProvider 静态初始化（BuildPlans 在 birthDate 非空时
    /// 会调用 DateTimeFormatter，该服务依赖 ServiceProvider 单例，不适合单元测试）。
    /// today 硬编码为 2025-06-29 以保证测试可重复，不依赖运行日期。
    /// </summary>
    [Fact]
    public void BuildTimeline_EmptyDb_CompletesWithin500ms()
    {
        DateTime? birthDate = null;  // 跳过 RecommendedDateText 计算，避免 ServiceProvider 依赖
        var today = new DateTime(2025, 6, 29);  // 硬编码保证可重复，不依赖运行日期
        var customVaccines = new List<CustomVaccine>();

        var sw = Stopwatch.StartNew();
        var vaccineRecords = _recordService.GetByType(RecordType.Vaccine, 1000);
        var plans = VaccineTimelineBuilder.BuildPlans(birthDate, customVaccines);
        var views = VaccineTimelineBuilder.BuildPlanViews(plans, vaccineRecords, birthDate, today);
        var groups = VaccineTimelineBuilder.BuildGroups(views);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 500,
            $"空库构建时间轴耗时 {sw.ElapsedMilliseconds}ms 超过 500ms 阈值");
        Assert.True(groups.Count > 0, "应至少有一个分组");
        Assert.True(views.Count > 30, $"应至少有 30 个剂次视图，实际 {views.Count}");
    }

    /// <summary>
    /// 基准测试：模拟 100 条历史疫苗记录下的构建耗时。
    /// 验证数据量增大时构建性能不会显著退化。
    /// </summary>
    [Fact]
    public void BuildTimeline_With100Records_CompletesWithin800ms()
    {
        DateTime? birthDate = null;
        var today = new DateTime(2025, 6, 29);

        // 插入 100 条历史疫苗记录
        for (int i = 0; i < 100; i++)
        {
            var dto = new ChildNotes.Shared.Dtos.VaccineRecordDto
            {
                Name = $"测试疫苗{i}",
                VaccineId = "hepb",
                DoseId = $"dose{(i % 3) + 1}",
                Category = "free",
                DoseLabel = $"第{(i % 3) + 1}剂",
                Disease = "乙肝",
                Status = "done",
                Skipped = false,
                Time = "2024-06-01 10:00",
            };
            _recordService.AddVaccine(dto);
        }

        var sw = Stopwatch.StartNew();
        var vaccineRecords = _recordService.GetByType(RecordType.Vaccine, 1000);
        var plans = VaccineTimelineBuilder.BuildPlans(birthDate, new List<CustomVaccine>());
        var views = VaccineTimelineBuilder.BuildPlanViews(plans, vaccineRecords, birthDate, today);
        var groups = VaccineTimelineBuilder.BuildGroups(views);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 800,
            $"100 条记录下构建耗时 {sw.ElapsedMilliseconds}ms 超过 800ms 阈值");
    }

    /// <summary>
    /// 重复构建基准测试：连续构建 10 次的平均耗时。
    /// 用于验证优化后（后台线程 + 预计算 bool 属性）的稳定性。
    /// </summary>
    [Fact]
    public void BuildTimeline_RepeatedBuild_AverageUnder200ms()
    {
        DateTime? birthDate = null;
        var today = new DateTime(2025, 6, 29);
        var customVaccines = new List<CustomVaccine>();

        // 预热（首次构建包含 JIT 开销）
        var warmupRecords = _recordService.GetByType(RecordType.Vaccine, 1000);
        var warmupPlans = VaccineTimelineBuilder.BuildPlans(birthDate, customVaccines);
        var warmupViews = VaccineTimelineBuilder.BuildPlanViews(warmupPlans, warmupRecords, birthDate, today);
        _ = VaccineTimelineBuilder.BuildGroups(warmupViews);

        // 正式测量：连续构建 10 次
        var timings = new List<long>();
        for (int i = 0; i < 10; i++)
        {
            var sw = Stopwatch.StartNew();
            var records = _recordService.GetByType(RecordType.Vaccine, 1000);
            var plans = VaccineTimelineBuilder.BuildPlans(birthDate, customVaccines);
            var views = VaccineTimelineBuilder.BuildPlanViews(plans, records, birthDate, today);
            var groups = VaccineTimelineBuilder.BuildGroups(views);
            sw.Stop();
            timings.Add(sw.ElapsedMilliseconds);
        }

        var avg = timings.Average();
        var max = timings.Max();
        Assert.True(avg < 200,
            $"10 次构建平均耗时 {avg:F1}ms 超过 200ms（最大 {max}ms，最小 {timings.Min()}ms）");
    }

    /// <summary>
    /// 验证 VaccinePlanView 的预计算 bool 属性与 StatusClass 一致性。
    /// 这是 AXAML 优化（用 bool 绑定替代转换器）的正确性保证。
    /// </summary>
    [Theory]
    [InlineData(VaccineDoseStatus.Done, nameof(VaccinePlanView.IsDone))]
    [InlineData(VaccineDoseStatus.Skipped, nameof(VaccinePlanView.IsSkipped))]
    [InlineData(VaccineDoseStatus.Replaced, nameof(VaccinePlanView.IsReplaced))]
    [InlineData(VaccineDoseStatus.Overdue, nameof(VaccinePlanView.IsOverdue))]
    [InlineData(VaccineDoseStatus.Due, nameof(VaccinePlanView.IsDue))]
    [InlineData(VaccineDoseStatus.Soon, nameof(VaccinePlanView.IsSoon))]
    [InlineData(VaccineDoseStatus.Pending, nameof(VaccinePlanView.IsPending))]
    public void PrecomputedBoolProperties_MatchStatusClass(string status, string expectedTrueProp)
    {
        var view = new VaccinePlanView { Status = status, StatusClass = status };

        // 所有状态 bool 属性
        var props = new Dictionary<string, bool>
        {
            [nameof(VaccinePlanView.IsDone)] = view.IsDone,
            [nameof(VaccinePlanView.IsSkipped)] = view.IsSkipped,
            [nameof(VaccinePlanView.IsReplaced)] = view.IsReplaced,
            [nameof(VaccinePlanView.IsOverdue)] = view.IsOverdue,
            [nameof(VaccinePlanView.IsDue)] = view.IsDue,
            [nameof(VaccinePlanView.IsSoon)] = view.IsSoon,
            [nameof(VaccinePlanView.IsPending)] = view.IsPending,
        };

        // 只有对应状态的 bool 应为 true，其余全为 false
        Assert.True(props[expectedTrueProp], $"状态 {status} 时 {expectedTrueProp} 应为 true");
        foreach (var kv in props)
        {
            if (kv.Key != expectedTrueProp)
            {
                Assert.False(kv.Value, $"状态 {status} 时 {kv.Key} 应为 false，但实际为 true");
            }
        }
    }

    /// <summary>
    /// 验证 NotHandled 属性与 Handled 互斥。
    /// AXAML 中 IsVisible="{Binding NotHandled}" 替代了原先的 IsVisible="{Binding !Handled}"。
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void NotHandled_IsOppositeOfHandled(bool handled, bool expectedNotHandled)
    {
        var view = new VaccinePlanView { Handled = handled };
        Assert.Equal(expectedNotHandled, view.NotHandled);
    }
}
