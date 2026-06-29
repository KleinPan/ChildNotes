using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ChildNotes.Core.Constants;
using ChildNotes.Core.Dtos;
using ChildNotes.Shared.Constants;
using ChildNotes.Shared.Dtos;
using ChildNotes.Core.Services;
using ChildNotes.Infrastructure.External;

namespace ChildNotes.Infrastructure.Services;

/// <summary>
/// AI 智能记服务：将自然语言文本解析为结构化育儿记录并保存。
/// 优先调用 DeepSeek 进行语义解析；失败时降级到基于规则的正则解析，保证可用性。
/// </summary>
public partial class AiNoteService : IAiNoteService
{
    private const string SystemPrompt = """
你是一名育儿记录解析助手。请将用户输入的自然语言文本解析为一条结构化的育儿记录，并仅输出 JSON。

支持的记录类型（recordType）：
- feed：喂养（瓶喂奶粉/母乳、亲喂）
- diaper：换尿布
- sleep：睡眠
- temperature：体温
- growth：身高体重
- supplement：补给用药/营养
- pump：吸奶
- complementary：辅食
- abnormal：异常症状
- activity：活动

字段说明（缺失字段使用 null，不要编造）：
- recordType: 必填，上面列表中的值
- recordSubType: 子类型，如 feed 的 bottle/breast/expressed；diaper 的 wet/dirty/both/dry；supplement 的 medicine/nutrition；activity 的 play/outdoor/exercise
- time: HH:mm 格式，未提及时间则使用 null（后端将取当前时间）
- amount: 数值型，单位 ml（用于 feed 瓶喂、pump 总量等）
- duration: 数值型，单位分钟（用于 sleep、activity）
- leftDuration / rightDuration: 数值型，分钟（用于 feed 亲喂、pump）
- temperature: 数值型，℃
- height: 数值型，cm
- weight: 数值型，kg
- diaperType: wet/dirty/both/dry（仅 diaper 类型使用，与 recordSubType 同义，二者任填其一）
- note: 备注信息（如性状、颜色、补充说明）
- summary: 一句话人类可读的总结（<=30 字）
- confidence: 解析置信度 0~1

输出要求：
1. 只输出一个 JSON 对象，不要任何额外文字、解释或 Markdown 代码块。
2. 数值字段使用数字类型，不要加引号。
3. 模糊不清的输入也要尽力给出最可能的类型，confidence 相应降低。
""";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly DeepSeekClient _ai;
    private readonly IRecordService _recordService;

    public AiNoteService(DeepSeekClient ai, IRecordService recordService)
    {
        _ai = ai;
        _recordService = recordService;
    }

    public async Task<AiNoteParseResponse> ParseAndSaveAsync(AiNoteParseRequest req, long? babyId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req?.Text))
            throw new Core.Exceptions.BusinessException("记录文本不能为空", 400);

        var text = req.Text.Trim();
        if (text.Length > 500)
            throw new Core.Exceptions.BusinessException("记录文本过长（最多 500 字）", 400);

        AiNoteParseResponse parsed;
        try
        {
            parsed = await ParseByAiAsync(text, ct);
        }
        catch (Exception)
        {
            // 降级到规则解析：保证可用性，置信度下调
            parsed = ParseByRules(text) with { Confidence = 0.4 };
        }

        // 时间未提供则使用当前时间
        var time = string.IsNullOrEmpty(parsed.Time)
            ? DateTime.Now.ToString("yyyy-MM-dd HH:mm")
            : NormalizeTime(parsed.Time);

        // 落库
        var dto = BuildDto(parsed, time);
        var id = await _recordService.AddRecordAsync(parsed.RecordType, dto, ct);
        parsed.Saved = true;
        parsed.RecordId = id;
        return parsed;
    }

    private async Task<AiNoteParseResponse> ParseByAiAsync(string text, CancellationToken ct)
    {
        var (raw, _) = await _ai.ChatAsync(SystemPrompt, text, ct);
        var json = ExtractJsonObject(raw);
        var parsed = JsonSerializer.Deserialize<AiNoteParseResponse>(json, JsonOpts)
            ?? throw new InvalidOperationException("AI 返回内容无法解析");
        if (string.IsNullOrEmpty(parsed.RecordType) || !RecordType.All.Contains(parsed.RecordType))
            throw new InvalidOperationException("AI 返回的记录类型无效");
        if (parsed.Confidence <= 0) parsed.Confidence = 0.8;
        return parsed;
    }

    /// <summary>从可能含 Markdown 包裹的文本中提取首个 JSON 对象。</summary>
    private static string ExtractJsonObject(string raw)
    {
        var s = raw.Trim();
        if (s.StartsWith("```"))
        {
            // 去掉 ```json 或 ``` 开头与结尾
            var firstNewline = s.IndexOf('\n');
            if (firstNewline > 0) s = s[(firstNewline + 1)..];
            var lastFence = s.LastIndexOf("```");
            if (lastFence >= 0) s = s[..lastFence];
            s = s.Trim();
        }
        var start = s.IndexOf('{');
        var end = s.LastIndexOf('}');
        if (start >= 0 && end > start)
            s = s[start..(end + 1)];
        return s;
    }

    /// <summary>基于正则的降级解析：覆盖最常见的表述。公开以便单元测试访问。</summary>
    public AiNoteParseResponse ParseByRules(string text)
    {
        // 1) 喂奶：奶量/亲喂时长
        var feedMlMatch = FeedAmountRegex().Match(text);
        if (feedMlMatch.Success)
        {
            return new AiNoteParseResponse
            {
                RecordType = RecordType.Feed,
                RecordSubType = FeedType.Bottle,
                Amount = int.TryParse(feedMlMatch.Groups[1].Value, out var a) ? a : null,
                Time = ExtractTime(text),
                Summary = "瓶喂 " + feedMlMatch.Groups[1].Value + "ml",
                Confidence = 0.6,
            };
        }

        var breastMatch = BreastRegex().Match(text);
        if (breastMatch.Success)
        {
            var left = int.TryParse(breastMatch.Groups[1].Value, out var l) ? l : 0;
            var right = int.TryParse(breastMatch.Groups[2].Value, out var r) ? r : 0;
            return new AiNoteParseResponse
            {
                RecordType = RecordType.Feed,
                RecordSubType = FeedType.Breast,
                LeftDuration = left,
                RightDuration = right,
                Time = ExtractTime(text),
                Summary = $"亲喂 左{left} 右{right}分钟",
                Confidence = 0.6,
            };
        }

        // 2) 换尿布：先判定尿布事件本身（含"尿布"/"换尿"/"嘘嘘"/"便便"/"拉屎"/"拉尿"/单独的"尿了"/"便了"等）
        if (text.Contains("尿布") || text.Contains("换尿") || text.Contains("嘘嘘") || text.Contains("便便") || text.Contains("拉屎") || text.Contains("拉尿")
            || text.Contains("又尿又拉") || text.Contains("又拉又尿")
            || Regex.IsMatch(text, @"(^|[^布])尿了") || Regex.IsMatch(text, @"(^|[^布])便了"))
        {
            // 注意：干爽判定要优先于"尿/便"关键词，否则"换尿布 干爽"里的"尿"字会被误判为嘘嘘
            if (text.Contains("干爽") || text.Contains("干燥"))
            {
                return new AiNoteParseResponse
                {
                    RecordType = RecordType.Diaper,
                    RecordSubType = DiaperType.Dry,
                    DiaperType = DiaperType.Dry,
                    Time = ExtractTime(text),
                    Summary = "换尿布 干爽",
                    Confidence = 0.6,
                };
            }
            // 把"尿布"/"换尿"这些动作词先剔除，避免污染 wet/dirty 判定
            var content = text.Replace("尿布", "").Replace("换尿", "");
            bool hasDirty = content.Contains("便") || content.Contains("屎") || content.Contains("拉");
            bool hasWet = content.Contains("尿") || content.Contains("嘘");
            string sub = (hasDirty, hasWet) switch
            {
                (true, true) => DiaperType.Both,
                (true, false) => DiaperType.Dirty,
                (false, true) => DiaperType.Wet,
                _ => DiaperType.Dry,
            };
            return new AiNoteParseResponse
            {
                RecordType = RecordType.Diaper,
                RecordSubType = sub,
                DiaperType = sub,
                Time = ExtractTime(text),
                Summary = "换尿布 " + sub,
                Confidence = 0.6,
            };
        }

        // 3) 睡眠
        var sleepMatch = SleepRegex().Match(text);
        if (sleepMatch.Success || text.Contains("睡觉") || text.Contains("入睡") || text.Contains("小睡"))
        {
            int? dur = sleepMatch.Success && int.TryParse(sleepMatch.Groups[1].Value, out var d) ? d : null;
            return new AiNoteParseResponse
            {
                RecordType = RecordType.Sleep,
                Duration = dur,
                Time = ExtractTime(text),
                Summary = dur.HasValue ? $"睡眠 {dur}分钟" : "睡眠",
                Confidence = 0.6,
            };
        }

        // 4) 体温
        var tempMatch = TempRegex().Match(text);
        if (tempMatch.Success)
        {
            var t = decimal.TryParse(tempMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null;
            return new AiNoteParseResponse
            {
                RecordType = RecordType.Temperature,
                Temperature = t,
                Time = ExtractTime(text),
                Summary = t.HasValue ? $"体温 {t}℃" : "体温",
                Confidence = 0.6,
            };
        }

        // 5) 身高体重
        var growthMatch = GrowthRegex().Match(text);
        if (growthMatch.Success)
        {
            decimal? h = decimal.TryParse(growthMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var hv) ? hv : null;
            decimal? w = growthMatch.Groups[2].Success && decimal.TryParse(growthMatch.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var wv) ? wv : null;
            return new AiNoteParseResponse
            {
                RecordType = RecordType.Growth,
                Height = h,
                Weight = w,
                Time = ExtractTime(text),
                Summary = $"身高{h}cm 体重{w}kg",
                Confidence = 0.55,
            };
        }

        // 兜底：作为异常记录的 note
        return new AiNoteParseResponse
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
        var m = TimeRegex().Match(text);
        if (m.Success)
        {
            var hh = int.TryParse(m.Groups[1].Value, out var h) ? h : -1;
            var mm = m.Groups[2].Success && int.TryParse(m.Groups[2].Value, out var mn) ? mn : 0;
            if (hh < 0 || hh > 23 || mm < 0 || mm > 59) return null;

            // 处理 12 小时制表述：晚上/下午/傍晚 +1~11 点 → +12；中午/正午 12 保持 12；其余不动
            // 凌晨/早上/上午 不调整（hh 已是 0~11）
            if (hh < 12)
            {
                bool isPm = text.Contains("晚上") || text.Contains("下午") || text.Contains("傍晚") || text.Contains("夜里") || text.Contains("夜晚");
                if (isPm) hh += 12;
            }
            // 中午12点/正午12点 保持 12
            return $"{hh:D2}:{mm:D2}";
        }
        return null;
    }

    private static string NormalizeTime(string time)
    {
        // 兼容 "HH:mm" / "HH点mm分" / "HH点" 等
        var m = TimeRegex().Match(time);
        if (m.Success)
        {
            var hh = int.TryParse(m.Groups[1].Value, out var h) ? h : DateTime.Now.Hour;
            var mm = m.Groups[2].Success ? (int.TryParse(m.Groups[2].Value, out var mn) ? mn : 0) : 0;
            return $"{hh:D2}:{mm:D2}";
        }
        if (DateTime.TryParse(time, out var dt)) return dt.ToString("yyyy-MM-dd HH:mm");
        return DateTime.Now.ToString("yyyy-MM-dd HH:mm");
    }

    /// <summary>根据解析结果构造对应类型的 DTO，复用现有 IRecordService 的 AddRecordAsync 入口。</summary>
    private static object BuildDto(AiNoteParseResponse p, string time)
    {
        return p.RecordType switch
        {
            RecordType.Feed => p.RecordSubType == FeedType.Breast
                ? new FeedRecordDto
                {
                    Type = FeedType.Breast,
                    Time = time,
                    LeftDuration = p.LeftDuration,
                    RightDuration = p.RightDuration,
                    LeftDurationSec = (p.LeftDuration ?? 0) * 60,
                    RightDurationSec = (p.RightDuration ?? 0) * 60,
                }
                : new FeedRecordDto
                {
                    Type = string.IsNullOrEmpty(p.RecordSubType) ? FeedType.Bottle : p.RecordSubType,
                    Time = time,
                    Amount = p.Amount,
                },
            RecordType.Diaper => new DiaperRecordDto
            {
                Type = string.IsNullOrEmpty(p.DiaperType) ? (p.RecordSubType ?? DiaperType.Dry) : p.DiaperType!,
                Time = time,
            },
            RecordType.Sleep => new SleepRecordDto
            {
                Time = time,
                Duration = p.Duration,
            },
            RecordType.Temperature => new TemperatureRecordDto
            {
                Temperature = p.Temperature ?? 0,
                IsAbnormal = (p.Temperature ?? 0) >= 37.3m,
                Note = p.Note,
                Time = time,
            },
            RecordType.Growth => new GrowthRecordDto
            {
                Height = p.Height,
                Weight = p.Weight,
                Time = time,
            },
            RecordType.Supplement => new SupplementRecordDto
            {
                Type = string.IsNullOrEmpty(p.RecordSubType) ? "medicine" : p.RecordSubType,
                Name = p.Note ?? "未命名",
                Time = time,
            },
            RecordType.Pump => new PumpRecordDto
            {
                TotalAmount = p.Amount,
                LeftDuration = p.LeftDuration,
                RightDuration = p.RightDuration,
                Note = p.Note,
                Time = time,
            },
            RecordType.Complementary => new ComplementaryRecordDto
            {
                FoodName = p.Note,
                Time = time,
            },
            RecordType.Abnormal => new AbnormalRecordDto
            {
                Temperature = p.Temperature,
                Note = p.Note,
                Time = time,
            },
            RecordType.Activity => new ActivityRecordDto
            {
                Name = p.Note ?? "活动",
                Category = p.RecordSubType,
                Duration = p.Duration,
                Time = time,
            },
            _ => new { Time = time, Note = p.Note },
        };
    }

    [GeneratedRegex(@"(\d+)\s*(?:ml|毫升|mL)")]
    private static partial Regex FeedAmountRegex();

    [GeneratedRegex(@"(?:左|left)\s*(\d+)\s*(?:分|min|分钟).*(?:右|right)\s*(\d+)\s*(?:分|min|分钟)")]
    private static partial Regex BreastRegex();

    [GeneratedRegex(@"(\d+)\s*(?:分|min|分钟)")]
    private static partial Regex SleepRegex();

    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*(?:℃|度)")]
    private static partial Regex TempRegex();

    [GeneratedRegex(@"(?:身高|高)\s*(\d+(?:\.\d+)?)\s*(?:cm|厘米)?.*(?:体重|重)\s*(\d+(?:\.\d+)?)\s*(?:kg|公斤|斤)?")]
    private static partial Regex GrowthRegex();

    [GeneratedRegex(@"(\d{1,2})\s*(?:点|:|：)\s*(\d{0,2})")]
    private static partial Regex TimeRegex();
}
