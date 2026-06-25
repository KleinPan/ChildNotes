using System.Text;
using System.Text.Json;
using ChildNotes.Data.Repositories;
using ChildNotes.Models;

namespace ChildNotes.Services;

public sealed class AiAnalysisService
{
    private readonly AiAnalysisRepository _repo;
    private readonly RecordService _recordService;
    private readonly BabyService _babyService;
    private readonly AppState _state;
    private readonly LlmClient _llmClient;

    private const string SkillPrompt = """
你是"宝宝成长记录"的智能分析助手。你会收到一份由后端整理的宝宝所选连续 7 天记录 TXT，内容可能包括喂养、睡眠、尿布/排便、体温、生长、辅食、用药、异常症状、疫苗、活动，以及与上一次分析或上一段 7 天记录的结构化对比。

输出中不要提及模型身份、能力来源或相关技术称呼。

请严格基于 TXT 中已有数据分析，不要编造未提供的信息。记录不足时要明确说明"数据不足"。如果 TXT 中包含"数据完整度提示"，请在"本周概览"或"需要留意"中自然提示家长，并给出接下来建议补充记录的方向。

如果 TXT 中包含"与上次分析对比"，请输出"与上次对比"小节。只比较 TXT 中列出的指标变化，例如记录天数、喂养次数、奶瓶/瓶喂总量、亲喂时长、睡眠时长、尿布/排便次数、异常/症状记录。没有上一次分析或上周基准数据时，不要强行比较，要说明"暂无足够上次数据，暂不能判断明显变化"。

输出要求：
1. 使用简体中文，语气温和、专业、适合家长阅读。
2. 不要输出诊断结论，不要替代医生判断，不要给出药物剂量或处方。
3. 如果发现持续高热、反复呕吐/腹泻、精神差、尿量明显减少、喂养显著下降、体重明显异常等风险信号，要放在"需要留意"中提示及时咨询儿科医生或就医。
4. 建议要可执行，优先给出家长接下来 7 天可以做的观察和记录动作。
5. 输出不要超过 1400 字，适合小程序详情页阅读。
6. 不要使用 Markdown 语法，不要使用 #、##、**、-、*、```；小标题请单独成行并以中文冒号结尾，条目请使用中文序号"1、2、3、"。

请按下面结构输出，不要使用表格，不要使用 Markdown 标题符号：

本周概览：
用 3-5 句话概括宝宝这 7 天的记录情况、主要规律和数据完整度。

与上次对比：
用 2-4 条说明本次与上一次分析/上周基准的主要变化。优先说明变化方向和幅度，例如奶量增加/减少、睡眠总时长变化、记录完整度变化、异常记录是否增多。没有足够上次数据时说明无法可靠比较。

重点观察：
列出 3-6 条从记录中看出的趋势或异常，例如喂养间隔、奶量、睡眠时长、尿布/排便、体温、辅食反应、生长变化等。

个性化建议：
围绕喂养、睡眠、排便/健康、生长发育四类给出具体建议。没有相关数据时说明需要补充记录。

需要留意：
只写基于记录中能看到的风险点；如果没有明显风险，也要提醒哪些情况出现时需要及时咨询医生。

接下来 7 天：
给出 3-5 条可执行的记录或照护建议，便于家长立刻照做。
""";

    public AiAnalysisService(AiAnalysisRepository repo, RecordService recordService, BabyService babyService, AppState state, LlmClient llmClient)
    {
        _repo = repo;
        _recordService = recordService;
        _babyService = babyService;
        _state = state;
        _llmClient = llmClient;
    }

    public LlmConfig GetLlmConfig() => _repo.GetLlmConfig();

    public void SaveLlmConfig(LlmConfig config) => _repo.SaveLlmConfig(config);

    public List<AiAnalysisRecord> ListRecords()
    {
        if (_state.CurrentBaby is null) return new();
        return _repo.GetByBaby(_state.CurrentBaby.Id);
    }

    public AiAnalysisRecord? GetRecord(long id) => _repo.FindById(id);

    public bool HasRangeAnalysis(DateTime start, DateTime end)
    {
        if (_state.CurrentBaby is null) return false;
        return _repo.FindByRange(_state.CurrentBaby.Id, start, end) is not null;
    }

    public async Task<AiAnalysisRecord> GenerateAsync(DateTime start, DateTime end)
    {
        var baby = _state.CurrentBaby ?? throw new InvalidOperationException("请先选择宝宝");

        var days = (end.Date - start.Date).Days + 1;
        if (days != 7)
            throw new InvalidOperationException("分析区间必须为连续 7 天");

        var existing = _repo.FindByRange(baby.Id, start, end);
        if (existing is not null)
            throw new InvalidOperationException("该区间已生成过分析，请查看历史记录");

        var records = _recordService.GetByDateRange(start, end);
        if (records.Count == 0)
            throw new InvalidOperationException("所选区间内暂无记录，无法生成分析");

        var sourceText = BuildSourceText(baby, start, end, records);
        var dataQualityTip = BuildDataQualityTip(records, start, end);

        var previous = GetPreviousAnalysis(baby.Id, start);
        if (previous is not null)
        {
            sourceText += "\n\n与上次分析对比：\n" + BuildComparisonText(previous, records);
        }

        var config = _repo.GetLlmConfig();
        var analysisText = await _llmClient.ChatAsync(config, SkillPrompt, sourceText);

        var record = new AiAnalysisRecord
        {
            UserId = _state.UserId,
            BabyId = baby.Id,
            BabyName = baby.Name,
            RangeStartDate = start,
            RangeEndDate = end,
            AnalysisText = analysisText,
            DataQualityTip = dataQualityTip,
            Model = config.ModelName,
        };
        record.Id = _repo.Insert(record);
        return record;
    }

    public void DeleteRecord(long id) => _repo.Delete(id);

    private AiAnalysisRecord? GetPreviousAnalysis(long babyId, DateTime currentStart)
    {
        var all = _repo.GetByBaby(babyId);
        return all.FirstOrDefault(r => r.RangeEndDate < currentStart);
    }

    private static string BuildSourceText(Baby baby, DateTime start, DateTime end, List<ChildRecord> records)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"宝宝姓名：{baby.Name}");
        sb.AppendLine($"性别：{(baby.Gender == "boy" ? "男" : "女")}");
        if (baby.BirthDate.HasValue)
        {
            var ageDays = (int)(start.Date - baby.BirthDate.Value).TotalDays;
            sb.AppendLine($"出生日期：{baby.BirthDate.Value:yyyy-MM-dd}（分析时约 {ageDays} 天）");
        }
        sb.AppendLine($"分析区间：{start:yyyy-MM-dd} 至 {end:yyyy-MM-dd}");
        sb.AppendLine($"记录总数：{records.Count} 条");
        sb.AppendLine();

        var byDate = records.GroupBy(r => r.RecordDate.Date).OrderBy(g => g.Key).ToList();
        foreach (var group in byDate)
        {
            sb.AppendLine($"【{group.Key:yyyy-MM-dd}】");
            foreach (var r in group.OrderBy(r => r.RecordTime))
            {
                sb.AppendLine($"  {r.RecordTime:HH:mm} {FormatRecordLine(r)}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("汇总统计：");
        sb.AppendLine(BuildAggregateStats(records));

        return sb.ToString();
    }

    private static string FormatRecordLine(ChildRecord r)
    {
        return r.RecordType switch
        {
            RecordType.Feed => r.RecordSubType == "breast"
                ? $"喂养-亲喂 左{(r.LeftDurationSec ?? 0) / 60}分 右{(r.RightDurationSec ?? 0) / 60}分"
                : $"喂养-瓶喂 {r.AmountMl ?? 0}ml",
            RecordType.Diaper => $"尿布-{r.RecordSubType}",
            RecordType.Sleep => $"睡眠 {(r.DurationSec ?? 0) / 60}分钟",
            RecordType.Temperature => $"体温 {r.TemperatureValue}℃{(r.AbnormalFlag == true ? "(发热)" : "")}",
            RecordType.Growth => $"生长 身高{r.HeightCm}cm 体重{r.WeightKg}kg",
            RecordType.Supplement => $"补充-{r.RecordSubType}",
            RecordType.Pump => $"吸奶 {r.AmountMl ?? 0}ml",
            RecordType.Complementary => "辅食",
            RecordType.Abnormal => $"异常-{r.RecordSubType}",
            RecordType.Activity => $"活动-{r.RecordSubType} {(r.DurationSec ?? 0) / 60}分钟",
            RecordType.Vaccine => "疫苗",
            RecordType.Milestone => "里程碑",
            RecordType.MaternalFood => $"妈妈饮食-{r.RecordSubType}",
            _ => r.RecordType,
        };
    }

    private static string BuildAggregateStats(List<ChildRecord> records)
    {
        var sb = new StringBuilder();
        var feeds = records.Where(r => r.RecordType == RecordType.Feed).ToList();
        var bottleMilk = feeds.Where(r => r.RecordSubType != "breast").Sum(r => r.AmountMl ?? 0);
        var breastMin = feeds.Where(r => r.RecordSubType == "breast").Sum(r => (r.DurationSec ?? 0) / 60);
        var sleeps = records.Where(r => r.RecordType == RecordType.Sleep).Sum(r => (r.DurationSec ?? 0) / 60);
        var diapers = records.Count(r => r.RecordType == RecordType.Diaper);
        var dirtyDiapers = records.Count(r => r.RecordType == RecordType.Diaper && r.RecordSubType is "dirty" or "both");
        var temps = records.Where(r => r.RecordType == RecordType.Temperature).ToList();
        var growths = records.Where(r => r.RecordType == RecordType.Growth).ToList();
        var abnormals = records.Count(r => r.RecordType == RecordType.Abnormal);

        sb.AppendLine($"  喂养次数：{feeds.Count}（瓶喂总量 {bottleMilk}ml，亲喂总时长 {breastMin}分钟）");
        sb.AppendLine($"  睡眠总时长：{sleeps / 60}小时{sleeps % 60}分钟");
        sb.AppendLine($"  尿布次数：{diapers}（其中排便 {dirtyDiapers} 次）");
        if (temps.Count > 0)
        {
            var latest = temps.OrderByDescending(r => r.RecordTime).First();
            sb.AppendLine($"  体温记录：{temps.Count} 次，最近 {latest.TemperatureValue}℃");
        }
        if (growths.Count > 0)
        {
            var latest = growths.OrderByDescending(r => r.RecordTime).First();
            sb.AppendLine($"  最近生长：身高 {latest.HeightCm}cm 体重 {latest.WeightKg}kg");
        }
        if (abnormals > 0)
            sb.AppendLine($"  异常记录：{abnormals} 次");
        return sb.ToString();
    }

    private static string BuildDataQualityTip(List<ChildRecord> records, DateTime start, DateTime end)
    {
        var recordedDays = records.Select(r => r.RecordDate.Date).Distinct().Count();
        var totalDays = (end.Date - start.Date).Days + 1;
        if (recordedDays < totalDays)
            return $"数据完整度：7天中有{recordedDays}天有记录，建议补充缺失日期的记录以获得更准确分析";
        return string.Empty;
    }

    private static string BuildComparisonText(AiAnalysisRecord previous, List<ChildRecord> currentRecords)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"上次分析区间：{previous.RangeStartDate:yyyy-MM-dd} 至 {previous.RangeEndDate:yyyy-MM-dd}");
        sb.AppendLine($"上次分析摘要：{previous.Preview}");
        sb.AppendLine("请对比本次与上次的喂养次数、奶量、睡眠时长、尿布次数、异常记录等指标变化。");
        return sb.ToString();
    }
}
