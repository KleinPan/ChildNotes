using System.Text.Json;
using System.Text.RegularExpressions;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Shared.Constants;

namespace ChildNotes.Services;

/// <summary>
/// AI 智能记解析服务：封装"自然语言文本 → 结构化记录"的三级降级解析逻辑。
/// 抽取自原 AiNote 模态的解析逻辑，供首页快捷输入框共用。
/// 三级降级顺序（按 AiSettings.NoteSource 配置）：
/// - local（默认）：本地 LLM → 后端接口 → 规则降级
/// - server：后端接口 → 本地 LLM → 规则降级
/// </summary>
public sealed class AiNoteParseService
{
    private readonly AiParseApiClient _apiClient = ServiceProvider.Instance.AiParseApiClient;
    private readonly AiAnalysisService _aiService = ServiceProvider.Instance.AiAnalysisService;
    private readonly LlmClient _llmClient = ServiceProvider.Instance.LlmClient;

    /// <summary>本地 LLM 系统提示词：规定输出 JSON 字段。</summary>
    internal const string LocalSystemPrompt = """
你是育儿记录解析助手。请将用户输入解析为一条结构化育儿记录，并仅输出 JSON。

支持的 recordType: feed / diaper / sleep / temperature / growth / supplement / pump / complementary / abnormal / activity
- feed 子类型: bottle/breast/expressed
- diaper 子类型(diaperType): wet/dirty/both/dry
- supplement 子类型: medicine/nutrition
- activity 子类型: play/outdoor/exercise

字段：recordType, recordSubType, time(HH:mm), amount(ml数值), duration(分钟),
leftDuration, rightDuration, temperature(℃), height(cm), weight(kg), diaperType,
note, summary(<=30字一句话), confidence(0~1)。

只输出 JSON，不要任何额外文字或 Markdown 代码块。缺失字段用 null。
""";

    /// <summary>
    /// 根据 NoteSource 配置选择解析路径并执行三级降级。
    /// 返回结果中 Saved 字段表示后端是否已落库；为 false 时调用方需自行保存。
    /// </summary>
    public async Task<AiNoteParseResult?> ParseAsync(string text)
    {
        var config = _aiService.GetLlmConfig();
        var preferServer = config.NoteSource == "server";

        // 1) 首选路径
        if (preferServer)
        {
            var remote = await TryServerAsync(text);
            if (remote is not null && !string.IsNullOrEmpty(remote.RecordType))
                return remote;
        }
        else
        {
            var local = await TryLocalLlmAsync(text, config);
            if (local is not null && !string.IsNullOrEmpty(local.RecordType))
                return local;
        }

        // 2) 降级到另一条 AI 路径
        if (preferServer)
        {
            var local = await TryLocalLlmAsync(text, config);
            if (local is not null && !string.IsNullOrEmpty(local.RecordType))
                return local;
        }
        else
        {
            var remote = await TryServerAsync(text);
            if (remote is not null && !string.IsNullOrEmpty(remote.RecordType))
                return remote;
        }

        // 3) 最终规则降级
        return LocalRuleParse(text);
    }

    /// <summary>尝试调用后端解析接口；未配置或失败时返回 null。</summary>
    private async Task<AiNoteParseResult?> TryServerAsync(string text)
    {
        if (string.IsNullOrEmpty(ServiceProvider.Instance.SyncConfigRepository.Get().ServerUrl))
            return null;
        try
        {
            return await _apiClient.ParseAsync(text);
        }
        catch (Exception ex)
        {
            DevLogger.Log("AiNote", "后端解析失败：" + ex.Message);
            return null;
        }
    }

    /// <summary>尝试调用用户配置的本地 LLM；未配置/未启用/失败时返回 null。</summary>
    private async Task<AiNoteParseResult?> TryLocalLlmAsync(string text, LlmConfig config)
    {
        if (config is null || !config.Enabled)
            return null;
        try
        {
            var raw = await _llmClient.ChatAsync(config, LocalSystemPrompt, text);
            var parsed = ParseLlmJson(raw);
            if (parsed is not null) parsed.Saved = false;
            return parsed;
        }
        catch (Exception ex)
        {
            DevLogger.Log("AiNote", "本地 LLM 解析失败：" + ex.Message);
            return null;
        }
    }

    /// <summary>从 LLM 返回内容中提取 JSON 并反序列化。</summary>
    private static AiNoteParseResult? ParseLlmJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        if (s.StartsWith("```"))
        {
            var nl = s.IndexOf('\n');
            if (nl > 0) s = s[(nl + 1)..];
            var last = s.LastIndexOf("```");
            if (last >= 0) s = s[..last];
            s = s.Trim();
        }
        var start = s.IndexOf('{');
        var end = s.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        s = s[start..(end + 1)];
        try
        {
            return JsonSerializer.Deserialize<AiNoteParseResult>(s, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
        }
        catch { return null; }
    }

    /// <summary>本地规则降级解析：覆盖最常见的育儿记录表述。</summary>
    public static AiNoteParseResult LocalRuleParse(string text)
    {
        // 喂奶：奶量
        var m = Regex.Match(text, @"(\d+)\s*(?:ml|毫升|mL)");
        if (m.Success && (text.Contains("奶") || text.Contains("喂") || text.Contains("吃")))
        {
            return new AiNoteParseResult
            {
                RecordType = RecordType.Feed,
                RecordSubType = "bottle",
                Amount = int.TryParse(m.Groups[1].Value, out var a) ? a : null,
                Time = ExtractTime(text),
                Summary = "瓶喂 " + m.Groups[1].Value + "ml",
                Confidence = 0.4,
            };
        }

        // 亲喂
        var bm = Regex.Match(text, @"(?:左|left)\s*(\d+)\s*(?:分|min|分钟).*(?:右|right)\s*(\d+)\s*(?:分|min|分钟)");
        if (bm.Success)
        {
            var l = int.TryParse(bm.Groups[1].Value, out var lv) ? lv : 0;
            var r = int.TryParse(bm.Groups[2].Value, out var rv) ? rv : 0;
            return new AiNoteParseResult
            {
                RecordType = RecordType.Feed,
                RecordSubType = "breast",
                LeftDuration = l,
                RightDuration = r,
                Time = ExtractTime(text),
                Summary = $"亲喂 左{l} 右{r}分钟",
                Confidence = 0.4,
            };
        }

        // 尿布
        if (text.Contains("尿布") || text.Contains("换尿") || text.Contains("嘘嘘") || text.Contains("便便") || text.Contains("拉屎") || text.Contains("拉尿")
            || text.Contains("又尿又拉") || Regex.IsMatch(text, @"(^|[^布])尿了") || Regex.IsMatch(text, @"(^|[^布])便了"))
        {
            if (text.Contains("干爽") || text.Contains("干燥"))
            {
                return new AiNoteParseResult
                {
                    RecordType = RecordType.Diaper,
                    DiaperType = "dry",
                    RecordSubType = "dry",
                    Time = ExtractTime(text),
                    Summary = "换尿布 干爽",
                    Confidence = 0.4,
                };
            }
            var content = text.Replace("尿布", "").Replace("换尿", "");
            bool hasDirty = content.Contains("便") || content.Contains("屎") || content.Contains("拉");
            bool hasWet = content.Contains("尿") || content.Contains("嘘");
            string sub = (hasDirty, hasWet) switch
            {
                (true, true) => "both",
                (true, false) => "dirty",
                (false, true) => "wet",
                _ => "dry",
            };
            return new AiNoteParseResult
            {
                RecordType = RecordType.Diaper,
                DiaperType = sub,
                RecordSubType = sub,
                Time = ExtractTime(text),
                Summary = "换尿布 " + sub,
                Confidence = 0.4,
            };
        }

        // 睡眠
        var sm = Regex.Match(text, @"(\d+)\s*(?:分|min|分钟)");
        if (sm.Success && (text.Contains("睡") || text.Contains("小睡") || text.Contains("入睡")))
        {
            return new AiNoteParseResult
            {
                RecordType = RecordType.Sleep,
                Duration = int.TryParse(sm.Groups[1].Value, out var d) ? d : null,
                Time = ExtractTime(text),
                Summary = sm.Groups[1].Value + "分钟睡眠",
                Confidence = 0.4,
            };
        }

        // 体温
        var tm = Regex.Match(text, @"(\d+(?:\.\d+)?)\s*(?:℃|度)");
        if (tm.Success && (text.Contains("体温") || text.Contains("烧")))
        {
            return new AiNoteParseResult
            {
                RecordType = RecordType.Temperature,
                Temperature = decimal.TryParse(tm.Groups[1].Value, out var t) ? t : null,
                Time = ExtractTime(text),
                Summary = "体温 " + tm.Groups[1].Value + "℃",
                Confidence = 0.4,
            };
        }

        // 身高体重
        var gm = Regex.Match(text, @"(?:身高|高)\s*(\d+(?:\.\d+)?)\s*(?:cm|厘米)?.*(?:体重|重)\s*(\d+(?:\.\d+)?)\s*(?:kg|公斤|斤)?");
        if (gm.Success)
        {
            return new AiNoteParseResult
            {
                RecordType = RecordType.Growth,
                Height = decimal.TryParse(gm.Groups[1].Value, out var h) ? h : null,
                Weight = decimal.TryParse(gm.Groups[2].Value, out var w) ? w : null,
                Time = ExtractTime(text),
                Summary = "身高 " + gm.Groups[1].Value + "cm 体重 " + gm.Groups[2].Value + "kg",
                Confidence = 0.35,
            };
        }

        // 兜底
        return new AiNoteParseResult
        {
            RecordType = RecordType.Activity,
            Note = text,
            Time = ExtractTime(text),
            Summary = "未识别记录",
            Confidence = 0.2,
        };
    }

    private static string? ExtractTime(string text)
    {
        var m = Regex.Match(text, @"(\d{1,2})\s*(?:点|:|：)\s*(\d{0,2})");
        if (m.Success)
        {
            var hh = int.TryParse(m.Groups[1].Value, out var h) ? h : -1;
            var mm = m.Groups[2].Success && int.TryParse(m.Groups[2].Value, out var mn) ? mn : 0;
            if (hh < 0 || hh > 23 || mm < 0 || mm > 59) return null;
            if (hh < 12 && (text.Contains("晚上") || text.Contains("下午") || text.Contains("傍晚") || text.Contains("夜里")))
                hh += 12;
            return $"{hh:D2}:{mm:D2}";
        }
        return null;
    }

    /// <summary>将解析结果按现有数据分类标准存储到本地数据库。</summary>
    public static void SaveLocally(AiNoteParseResult r, string originalText, RecordService recordService)
    {
        var time = string.IsNullOrEmpty(r.Time) ? DateTime.Now.ToString("O") : NormalizeTime(r.Time);
        switch (r.RecordType)
        {
            case RecordType.Feed:
                if (r.RecordSubType == "breast")
                {
                    recordService.AddFeed(new ChildNotes.Shared.Dtos.FeedRecordDto
                    {
                        Type = "breast",
                        Time = time,
                        LeftDuration = r.LeftDuration,
                        RightDuration = r.RightDuration,
                        LeftDurationSec = (r.LeftDuration ?? 0) * 60,
                        RightDurationSec = (r.RightDuration ?? 0) * 60,
                    });
                }
                else
                {
                    recordService.AddFeed(new ChildNotes.Shared.Dtos.FeedRecordDto
                    {
                        Type = string.IsNullOrEmpty(r.RecordSubType) ? "bottle" : r.RecordSubType,
                        Time = time,
                        Amount = r.Amount,
                    });
                }
                break;
            case RecordType.Diaper:
                recordService.AddDiaper(new ChildNotes.Shared.Dtos.DiaperRecordDto
                {
                    Type = string.IsNullOrEmpty(r.DiaperType) ? (r.RecordSubType ?? "dry") : r.DiaperType,
                    Time = time,
                });
                break;
            case RecordType.Sleep:
                recordService.AddSleep(new ChildNotes.Shared.Dtos.SleepRecordDto
                {
                    Time = time,
                    Duration = r.Duration,
                });
                break;
            case RecordType.Temperature:
                recordService.AddTemperature(new ChildNotes.Shared.Dtos.TemperatureRecordDto
                {
                    Temperature = r.Temperature ?? 0,
                    IsAbnormal = (r.Temperature ?? 0) >= 37.3m,
                    Note = r.Note,
                    Time = time,
                });
                break;
            case RecordType.Growth:
                recordService.AddGrowth(new ChildNotes.Shared.Dtos.GrowthRecordDto
                {
                    Height = r.Height,
                    Weight = r.Weight,
                    Time = time,
                });
                break;
            case RecordType.Supplement:
                recordService.AddSupplement(new ChildNotes.Shared.Dtos.SupplementRecordDto
                {
                    Type = string.IsNullOrEmpty(r.RecordSubType) ? "medicine" : r.RecordSubType,
                    Name = r.Note ?? "AI 识别",
                    Time = time,
                });
                break;
            case RecordType.Pump:
                recordService.AddPump(new ChildNotes.Shared.Dtos.PumpRecordDto
                {
                    TotalAmount = r.Amount,
                    LeftDuration = r.LeftDuration,
                    RightDuration = r.RightDuration,
                    Note = r.Note,
                    Time = time,
                });
                break;
            case RecordType.Complementary:
                recordService.AddComplementary(new ChildNotes.Shared.Dtos.ComplementaryRecordDto
                {
                    FoodName = r.Note,
                    Time = time,
                });
                break;
            case RecordType.Abnormal:
                recordService.AddAbnormal(new ChildNotes.Shared.Dtos.AbnormalRecordDto
                {
                    Temperature = r.Temperature,
                    Note = r.Note,
                    Time = time,
                });
                break;
            case RecordType.Activity:
                recordService.AddActivity(new ChildNotes.Shared.Dtos.ActivityRecordDto
                {
                    Name = r.Note ?? originalText,
                    Category = r.RecordSubType,
                    Duration = r.Duration,
                    Time = time,
                });
                break;
            default:
                // 未知类型作为 activity 兜底，原文保留在 Name
                recordService.AddActivity(new ChildNotes.Shared.Dtos.ActivityRecordDto
                {
                    Name = originalText,
                    Time = time,
                });
                break;
        }
    }

    private static string NormalizeTime(string time)
    {
        if (DateTime.TryParse(time, out var dt)) return dt.ToString("O");
        // HH:mm -> 补全日期
        if (System.Text.RegularExpressions.Regex.IsMatch(time, @"^\d{1,2}:\d{2}$"))
        {
            return DateTime.Today.Add(TimeSpan.Parse(time)).ToString("O");
        }
        return DateTime.Now.ToString("O");
    }
}
