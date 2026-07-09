using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ChildNotes.Core.Constants;
using ChildNotes.Core.Dtos;
using ChildNotes.Core.Exceptions;
using ChildNotes.Core.Services;
using ChildNotes.Infrastructure.External;
using ChildNotes.Shared.Constants;
using ChildNotes.Shared.Dtos;
using ChildNotes.Shared.Services;
using Microsoft.Extensions.Logging;

namespace ChildNotes.Infrastructure.Services;

/// <summary>
/// AI 智能记服务：将自然语言文本解析为一条或多条结构化育儿记录。
/// 优先调用 DeepSeek 进行语义解析；失败时降级到基于规则的正则解析，保证可用性。
/// 支持复合语句切分（如"睡了一觉，喝了奶，换了尿布"→3条记录）。
/// 注意：本服务仅做解析，不落库；调用方需自行持久化。
/// </summary>
public partial class AiNoteService : IAiNoteService
{
    private readonly ILogger<AiNoteService> _logger;
    private readonly DeepSeekClient _ai;

    private const string SystemPrompt = """
你是一名育儿记录解析助手。请将用户输入的自然语言文本解析为一条或多条结构化的育儿记录，并仅输出 JSON 数组。

支持的记录类型（recordType）：
- feed：喂奶（仅限奶类：瓶喂奶粉/母乳、亲喂；喝水不属于 feed）
- diaper：换尿布（含大小便相关表述，如"大便/便便/拉屎/拉了/臭臭/粑粑/拉臭/尿尿/嘘嘘"均归此类型）
- sleep：睡眠
- temperature：体温
- growth：身高体重
- supplement：补给（用药/营养补充，如维D、益生菌、药品等；不含喝水）
- water：喝水（独立类型，便于统计每日饮水量；amount=水量ml）
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
- name: supplement 专用，药品/营养品名称（如"宝泰康颗粒"、"伊可新"、"维D"），不含剂量
- dose: supplement 专用，剂量数值文本（如"0.5"、"1"、"5"），不含单位
- doseUnit: supplement 专用，剂量单位（如"包"、"粒"、"ml"、"滴"），与 dose 分开
- note: 备注信息（如性状、颜色、补充说明；supplement 不要把 name/dose 塞进 note）
- summary: 一句话人类可读的总结（<=30 字）
- confidence: 解析置信度 0~1

输出要求：
1. 输出一个 JSON 数组 [...]，每个元素是一个记录对象。
2. 若输入只描述一件事，仍输出单元素数组 [{...}]。
3. 若输入描述多件事（用逗号、连词分隔），每件事对应数组中的一个元素。
4. 只输出 JSON 数组，不要任何额外文字、解释或 Markdown 代码块。
5. 数值字段使用数字类型，不要加引号。
6. 模糊不清的输入也要尽力给出最可能的类型，confidence 相应降低。

示例：
输入"11点半睡到12点40，吃了130奶粉，喝10ml水"应输出：
[
  {"recordType":"sleep","time":"23:30","duration":70,"summary":"睡眠70分钟","confidence":0.9},
  {"recordType":"feed","recordSubType":"bottle","amount":130,"time":null,"summary":"瓶喂130ml","confidence":0.9},
  {"recordType":"water","amount":10,"time":null,"summary":"喝水10ml","confidence":0.8}
]

输入"拉了大便"应输出：
[{"recordType":"diaper","diaperType":"dirty","recordSubType":"dirty","note":"大便","summary":"换尿布 大便","confidence":0.9}]

输入"换尿布 便便"应输出：
[{"recordType":"diaper","diaperType":"dirty","recordSubType":"dirty","summary":"换尿布 便便","confidence":0.9}]

输入"吃了半包宝泰康颗粒"应输出：
[{"recordType":"supplement","recordSubType":"medicine","name":"宝泰康颗粒","dose":"0.5","doseUnit":"包","summary":"用药 宝泰康颗粒 半包","confidence":0.9}]

关键规则：
- "喝奶/吃奶/喂奶" → feed；"喝水/喝10ml水" → water（amount=水量ml）
- "吃药/吃半包XX颗粒" → supplement/medicine（name=药品名，dose=数值如"0.5"，doseUnit=单位如"包"）；"维D/益生菌" → supplement/nutrition
- "大便/便便/拉屎/拉了/臭臭/粑粑/拉臭" → diaper/dirty；"尿尿/嘘嘘/尿了" → diaper/wet；"又尿又拉" → diaper/both
- 时间"11点半"=11:30，"半"在分钟位表示30分
""";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public AiNoteService(DeepSeekClient ai, ILogger<AiNoteService> logger)
    {
        _ai = ai;
        _logger = logger;
    }

    public async Task<AiNoteParseBatchResponse> ParseAsync(AiNoteParseRequest req, string? babyId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req?.Text))
            throw new BusinessException("记录文本不能为空", 400);

        var text = req.Text.Trim();
        if (text.Length > 500)
            throw new BusinessException("记录文本过长（最多 500 字）", 400);

        // [AI-LOG] 用户输入完整记录：时间戳 + 输入类型 + 具体内容，便于问题分析与行为追踪
        _logger.LogInformation("[AI-LOG] 用户输入 | 时间={Time} 类型=NoteParse 文本={Text}",
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), text);

        List<AiNoteParseItem> items;
        try
        {
            items = await ParseByAiAsync(text, ct);
        }
        catch (Exception ex)
        {
            // 降级到规则解析：保证可用性，置信度下调
            _logger.LogWarning(ex, "[AI-LOG] AI 解析失败，降级到规则兜底。Text={Text}", text);
            items = ParseByRulesMulti(text);
            foreach (var it in items)
            {
                it.Confidence = 0.4;
                it.Source = ParseSource.Rule;
            }
        }

        // 时间未提供则使用当前时间（对每个 Item 应用）
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        foreach (var it in items)
        {
            it.Time = string.IsNullOrEmpty(it.Time) ? now : NormalizeTime(it.Time);
        }

        _logger.LogInformation("[AI-LOG] 解析完成 Items={Count} FirstType={FirstType} FirstSubType={FirstSubType} Text={Text}",
            items.Count,
            items.FirstOrDefault()?.RecordType ?? "-",
            items.FirstOrDefault()?.RecordSubType ?? "-",
            text);
        return new AiNoteParseBatchResponse { Items = items };
    }

    private async Task<List<AiNoteParseItem>> ParseByAiAsync(string text, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var (raw, _) = await _ai.ChatAsync(SystemPrompt, text, ct);
        sw.Stop();
        _logger.LogInformation("[AI-LOG] DeepSeek 调用成功 {Ms}ms respLen={Len}", sw.ElapsedMilliseconds, raw?.Length ?? 0);

        var json = ExtractJsonArray(raw ?? "");
        var items = JsonSerializer.Deserialize<List<AiNoteParseItem>>(json, JsonOpts)
            ?? throw new InvalidOperationException("AI 返回内容无法解析为列表");

        // 校验每条记录的 RecordType
        var valid = new List<AiNoteParseItem>(items.Count);
        foreach (var it in items)
        {
            if (string.IsNullOrEmpty(it.RecordType) || !RecordType.All.Contains(it.RecordType))
            {
                _logger.LogWarning("[AI-LOG] AI 返回的记录类型无效，已剔除：{RecordType}", it.RecordType);
                continue;
            }
            if (it.Confidence <= 0) it.Confidence = 0.8;
            it.Source = ParseSource.Ai;
            valid.Add(it);
        }
        if (valid.Count == 0)
            throw new InvalidOperationException("AI 返回的所有记录类型均无效");
        return valid;
    }

    /// <summary>从可能含 Markdown 包裹的文本中提取 JSON 数组段；兼容单对象返回。</summary>
    private static string ExtractJsonArray(string raw)
    {
        var s = raw.Trim();
        // 去除 ```json 或 ``` 包裹
        if (s.StartsWith("```"))
        {
            var firstNewline = s.IndexOf('\n');
            if (firstNewline > 0) s = s[(firstNewline + 1)..];
            var lastFence = s.LastIndexOf("```");
            if (lastFence >= 0) s = s[..lastFence];
            s = s.Trim();
        }

        // 优先找数组
        var arrStart = s.IndexOf('[');
        var arrEnd = s.LastIndexOf(']');
        if (arrStart >= 0 && arrEnd > arrStart)
            return s[arrStart..(arrEnd + 1)];

        // 兼容 LLM 返回单对象的情况：包裹成单元素数组
        var objStart = s.IndexOf('{');
        var objEnd = s.LastIndexOf('}');
        if (objStart >= 0 && objEnd > objStart)
            return "[" + s[objStart..(objEnd + 1)] + "]";

        return "[]";
    }

    /// <summary>
    /// 基于规则的多条解析：先用 NoteSplitter 切分复合语句，再对每段调 ParseByRules。
    /// 若切分后所有段都解析失败，回退到对原句调一次 ParseByRules，避免切分破坏整体语义。
    /// </summary>
    public List<AiNoteParseItem> ParseByRulesMulti(string text)
    {
        var segments = NoteSplitter.Split(text);
        if (segments.Count == 0)
            return new List<AiNoteParseItem> { ParseByRules(text) };

        var results = new List<AiNoteParseItem>(segments.Count);
        foreach (var seg in segments)
        {
            var item = ParseByRules(seg);
            // 兜底未识别（activity + 原文）的段不加入结果，避免噪声
            // 但如果整句只有一段且未识别，仍需返回兜底
            if (item.RecordType == RecordType.Activity && item.Confidence <= 0.2 && segments.Count > 1)
                continue;
            results.Add(item);
        }
        // 若全部段都被跳过（罕见），回退到原句解析
        if (results.Count == 0)
            results.Add(ParseByRules(text));
        return results;
    }

    /// <summary>基于正则的降级解析：覆盖最常见的表述。公开以便单元测试访问。</summary>
    public AiNoteParseItem ParseByRules(string text)
    {
        // 0) water（喝水）：必须在 feed/supplement 之前判定，避免"喝10ml水"被误判为喂奶或补给
        if (IsWaterLike(text))
        {
            return ParseWater(text);
        }

        // 0b) supplement（用药/营养）：必须在 feed 之前判定，避免"喝了半包药"被误判为喂奶
        // 判定条件：含剂型关键词（颗粒/冲剂/糖浆/滴剂/药片/胶囊/丸），
        // 或含"包/粒/滴"单位且无显式容量单位（ml/毫升）
        if (IsSupplementLike(text))
        {
            return ParseSupplement(text);
        }

        // 1) 喂奶：奶量/亲喂时长
        var feedMlMatch = FeedAmountRegex().Match(text);
        if (feedMlMatch.Success)
        {
            int? amount = int.TryParse(feedMlMatch.Groups[1].Value, out var a) ? a : null;
            var unit = feedMlMatch.Groups[2].Value; // ml / 毫升 / 奶粉 / 奶 / 母乳
            var hasExplicitUnit = unit is "ml" or "毫升" or "mL";
            return new AiNoteParseItem
            {
                RecordType = RecordType.Feed,
                RecordSubType = FeedType.Bottle,
                Amount = amount,
                Time = ExtractTime(text),
                Summary = "瓶喂 " + amount + (hasExplicitUnit ? "ml" : ""),
                Confidence = hasExplicitUnit ? 0.6 : 0.5,
            };
        }

        var breastMatch = BreastRegex().Match(text);
        if (breastMatch.Success)
        {
            var left = int.TryParse(breastMatch.Groups[1].Value, out var l) ? l : 0;
            var right = int.TryParse(breastMatch.Groups[2].Value, out var r) ? r : 0;
            return new AiNoteParseItem
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

        // 2) 换尿布（含大便/小便相关各种口语表述）
        if (text.Contains("尿布") || text.Contains("换尿") || text.Contains("嘘嘘") || text.Contains("便便") || text.Contains("拉屎") || text.Contains("拉尿")
            || text.Contains("又尿又拉") || text.Contains("又拉又尿")
            || text.Contains("大便") || text.Contains("小便") || text.Contains("拉了") || text.Contains("臭臭")
            || text.Contains("粑粑") || text.Contains("拉臭") || text.Contains("尿尿")
            || Regex.IsMatch(text, @"(^|[^布])尿了") || Regex.IsMatch(text, @"(^|[^布])便了"))
        {
            if (text.Contains("干爽") || text.Contains("干燥"))
            {
                return new AiNoteParseItem
                {
                    RecordType = RecordType.Diaper,
                    RecordSubType = DiaperType.Dry,
                    DiaperType = DiaperType.Dry,
                    Time = ExtractTime(text),
                    Summary = "换尿布 干爽",
                    Confidence = 0.6,
                };
            }
            var content = text.Replace("尿布", "").Replace("换尿", "").Replace("小便", "");
            // 大便判定：含"便/屎/粑/臭"或单独"拉"字（"拉"在育儿语境默认指大便）
            // 注意：content 已 Replace 掉"尿布/换尿/小便"，避免"小便"含"便"字被误判为 dirty
            bool hasDirty = content.Contains("便") || content.Contains("屎") || content.Contains("粑")
                || content.Contains("臭") || content.Contains("拉");
            // 小便判定：content 中"尿"字（"尿布/换尿"已 Replace），或显式"嘘嘘/小便"
            bool hasWet = content.Contains("尿") || text.Contains("嘘") || text.Contains("小便");
            string sub = (hasDirty, hasWet) switch
            {
                (true, true) => DiaperType.Both,
                (true, false) => DiaperType.Dirty,
                (false, true) => DiaperType.Wet,
                _ => DiaperType.Dry,
            };
            return new AiNoteParseItem
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
        if (sleepMatch.Success || text.Contains("睡觉") || text.Contains("入睡") || text.Contains("小睡") || text.Contains("睡到"))
        {
            int? dur = sleepMatch.Success && int.TryParse(sleepMatch.Groups[1].Value, out var d) ? d : null;
            // "11点半睡到12点40"这种带"睡到"的，尝试计算时长
            if (!dur.HasValue && text.Contains("睡到"))
            {
                dur = TryCalcSleepDuration(text);
            }
            return new AiNoteParseItem
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
            return new AiNoteParseItem
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
            return new AiNoteParseItem
            {
                RecordType = RecordType.Growth,
                Height = h,
                Weight = w,
                Time = ExtractTime(text),
                Summary = $"身高{h}cm 体重{w}kg",
                Confidence = 0.55,
            };
        }

        // 兜底：无法识别具体类型时作为 Activity 记录，原文存入 Note 字段
        return new AiNoteParseItem
        {
            RecordType = RecordType.Activity,
            Note = text,
            Time = ExtractTime(text),
            Summary = "未识别记录",
            Confidence = 0.2,
        };
    }

    /// <summary>判断文本是否为喝水（water）。</summary>
    private static bool IsWaterLike(string text)
    {
        // 含"水"且含"喝"即为喝水（排除"奶水/糖浆"等干扰——糖浆已被 supplement 前置判定）
        // 注意：本方法在 ParseByRules 中先于 supplement/feed 调用
        return text.Contains("水") && text.Contains("喝");
    }

    /// <summary>解析喝水记录。</summary>
    private static AiNoteParseItem ParseWater(string text)
    {
        // 提取水量（ml/毫升）
        int? amountMl = null;
        var mlMatch = Regex.Match(text, @"(\d+)\s*(?:ml|毫升|mL)");
        if (mlMatch.Success && int.TryParse(mlMatch.Groups[1].Value, out var ml))
            amountMl = ml;

        return new AiNoteParseItem
        {
            RecordType = RecordType.Water,
            Amount = amountMl,
            Time = ExtractTime(text),
            Summary = amountMl.HasValue ? $"喝水{amountMl}ml" : "喝水",
            Confidence = 0.6,
        };
    }

    /// <summary>判断文本是否为用药/营养补充（supplement）。</summary>
    private static bool IsSupplementLike(string text)
    {
        // 剂型关键词：含这些词基本可判定为 supplement
        if (text.Contains("颗粒") || text.Contains("冲剂") || text.Contains("糖浆")
            || text.Contains("滴剂") || text.Contains("药片") || text.Contains("胶囊")
            || text.Contains("药丸") || text.Contains("吃药") || text.Contains("服药"))
            return true;

        // 营养补充关键词
        if (text.Contains("维D") || text.Contains("维D3") || text.Contains("D3")
            || text.Contains("益生菌") || text.Contains("鱼肝油") || text.Contains("钙剂")
            || text.Contains("补钙") || text.Contains("补铁") || text.Contains("补锌"))
            return true;

        // 含"包/粒/滴"单位 + 药品/营养品语义词（排除"奶包"等干扰）
        if ((text.Contains("包") || text.Contains("粒") || text.Contains("滴"))
            && (text.Contains("喝") || text.Contains("吃") || text.Contains("服"))
            && !text.Contains("奶"))
            return true;

        return false;
    }

    /// <summary>解析用药/营养补充记录。</summary>
    private static AiNoteParseItem ParseSupplement(string text)
    {
        // 区分 medicine / nutrition
        // nutrition：营养补充剂（维D/益生菌等；喝水已由 ParseWater 提前处理）
        bool isNutrition = text.Contains("维D") || text.Contains("维D3") || text.Contains("D3")
            || text.Contains("益生菌") || text.Contains("鱼肝油") || text.Contains("钙剂")
            || text.Contains("补钙") || text.Contains("补铁") || text.Contains("补锌");
        string sub = isNutrition ? "nutrition" : "medicine";

        // 提取剂量（如"半包"、"1粒"、"5滴"、"10ml"）→ 拆分为 dose(数值) + doseUnit(单位)
        string? dose = null;
        string? doseUnit = null;
        int? amountMl = null;
        var dosageMatch = Regex.Match(text, @"(半包|半粒|半滴|\d+(?:\.\d+)?\s*(?:ml|毫升|包|粒|滴|片|丸))");
        if (dosageMatch.Success)
        {
            var dosage = dosageMatch.Groups[1].Value;
            // 拆分数值与单位
            var splitMatch = Regex.Match(dosage, @"^(\d+(?:\.\d+)?)\s*(ml|毫升|包|粒|滴|片|丸)$");
            if (splitMatch.Success)
            {
                dose = splitMatch.Groups[1].Value;
                doseUnit = splitMatch.Groups[2].Value == "毫升" ? "ml" : splitMatch.Groups[2].Value;
                if (doseUnit == "ml" && int.TryParse(dose, out var ml))
                    amountMl = ml;
            }
            else if (dosage == "半包") { dose = "0.5"; doseUnit = "包"; }
            else if (dosage == "半粒") { dose = "0.5"; doseUnit = "粒"; }
            else if (dosage == "半滴") { dose = "0.5"; doseUnit = "滴"; }
            else { dose = dosage; } // 兜底：整体作为 dose 文本
        }

        // 提取药品/营养品名称：去掉时间、剂量、动作词后的剩余内容
        var name = ExtractSupplementName(text);

        // Summary 用人类可读的"剂量+单位"拼接（如"半包 宝泰康颗粒"）
        var doseDisplay = (dose, doseUnit) switch
        {
            ("0.5", "包") => "半包",
            ("0.5", "粒") => "半粒",
            ("0.5", "滴") => "半滴",
            (string d, string u) => d + u,
            (string d, null) => d,
            _ => "",
        };

        return new AiNoteParseItem
        {
            RecordType = RecordType.Supplement,
            RecordSubType = sub,
            Amount = amountMl,
            Name = name,
            Dose = dose,
            DoseUnit = doseUnit,
            Note = string.IsNullOrEmpty(name) ? text : null,
            Time = ExtractTime(text),
            Summary = (string.IsNullOrEmpty(doseDisplay) ? "" : doseDisplay + " ") + (name ?? (isNutrition ? "补充剂记录" : "用药记录")),
            Confidence = 0.5,
        };
    }

    /// <summary>从文本中提取药品/营养品名称（去掉时间、时段词、剂量、动作词）。</summary>
    private static string? ExtractSupplementName(string text)
    {
        var s = text;
        // 去掉时间（含"8:17"、"8点17"等）
        s = TimeRegex().Replace(s, "");
        // 去掉时段词（"早上/早晨/上午/中午/下午/傍晚/晚上/夜里/夜间/半夜"）
        s = Regex.Replace(s, @"早上|早晨|上午|中午|下午|傍晚|晚上|夜里|夜间|半夜|今早|今晚|昨日|明天", "");
        // 去掉剂量
        s = Regex.Replace(s, @"半包|半粒|半滴|\d+(?:\.\d+)?\s*(?:ml|毫升|包|粒|滴|片|丸)", "");
        // 去掉动作词
        s = Regex.Replace(s, @"喝了|喝了|吃了|服用|服了|喝|吃|服", "");
        // 去掉常见停用词
        s = Regex.Replace(s, @"的|了|一点|一些", "");
        s = s.Trim(' ', '，', ',', '。');
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    /// <summary>尝试从"X点Y睡到A点B"格式计算睡眠时长（分钟）。支持"半"表示30分。</summary>
    private static int? TryCalcSleepDuration(string text)
    {
        // 支持"11点半睡到12点40"、"11:30睡到12:40"、"3点睡到5点"等格式
        // "半" 作为分钟的特殊写法，等价于 30
        var m = Regex.Match(text, @"(\d{1,2})\s*(?:点|:|：)\s*(半|\d{0,2})\s*睡到\s*(\d{1,2})\s*(?:点|:|：)\s*(半|\d{0,2})");
        if (!m.Success) return null;
        if (!int.TryParse(m.Groups[1].Value, out var sh)) return null;
        var sm = ParseMinuteGroup(m.Groups[2]);
        if (!int.TryParse(m.Groups[3].Value, out var eh)) return null;
        var em = ParseMinuteGroup(m.Groups[4]);

        // 处理 12 小时制（含晚上/下午等修饰词则 +12）
        if (sh < 12 && (text.Contains("晚上") || text.Contains("下午") || text.Contains("傍晚") || text.Contains("夜里")))
            sh += 12;
        if (eh < 12 && (text.Contains("晚上") || text.Contains("下午") || text.Contains("傍晚") || text.Contains("夜里")))
        {
            // 只有结束时间跨越到下午才 +12（如 11点半睡到12点40，若含"晚上"则都 +12）
            // 简化处理：若开始时间已 +12，结束时间小于开始时间则 +12
            if (eh < sh) eh += 12;
        }
        // 若结束小时小于开始小时，说明跨午夜
        if (eh < sh) eh += 24;

        var startMin = sh * 60 + sm;
        var endMin = eh * 60 + em;
        var dur = endMin - startMin;
        return dur > 0 ? dur : null;
    }

    /// <summary>解析分钟组：支持"半"=30、空=0、数字=数字。</summary>
    private static int ParseMinuteGroup(System.Text.RegularExpressions.Group g)
    {
        if (!g.Success) return 0;
        var v = g.Value;
        if (string.IsNullOrEmpty(v)) return 0;
        if (v == "半") return 30;
        return int.TryParse(v, out var n) ? n : 0;
    }

    private static string? ExtractTime(string text)
    {
        var m = TimeRegex().Match(text);
        if (m.Success)
        {
            var hh = int.TryParse(m.Groups[1].Value, out var h) ? h : -1;
            var mm = ParseMinuteGroup(m.Groups[2]);
            if (hh < 0 || hh > 23 || mm < 0 || mm > 59) return null;

            // 处理 12 小时制表述：晚上/下午/傍晚 +1~11 点 → +12；中午/正午 12 保持 12；其余不动
            if (hh < 12)
            {
                bool isPm = text.Contains("晚上") || text.Contains("下午") || text.Contains("傍晚") || text.Contains("夜里") || text.Contains("夜晚");
                if (isPm) hh += 12;
            }
            return $"{hh:D2}:{mm:D2}";
        }
        return null;
    }

    private static string NormalizeTime(string time)
    {
        var m = TimeRegex().Match(time);
        if (m.Success)
        {
            var hh = int.TryParse(m.Groups[1].Value, out var h) ? h : DateTime.Now.Hour;
            var mm = ParseMinuteGroup(m.Groups[2]);
            return $"{hh:D2}:{mm:D2}";
        }
        if (DateTime.TryParse(time, out var dt)) return dt.ToString("yyyy-MM-dd HH:mm");
        return DateTime.Now.ToString("yyyy-MM-dd HH:mm");
    }

    /// <summary>根据解析结果构造对应类型的 DTO（后端不落库，保留供测试使用）。</summary>
    private static object BuildDto(AiNoteParseItem p, string time)
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

    [GeneratedRegex(@"(\d+)\s*(ml|毫升|mL|奶粉|母乳|奶)")]
    private static partial Regex FeedAmountRegex();

    // 右侧时间单位可选（"左10右15" 省略中间单位也允许；末尾统一带单位）
    [GeneratedRegex(@"(?:左|left)\s*(\d+)\s*(?:分|min|分钟)?.*?(?:右|right)\s*(\d+)\s*(?:分|min|分钟)?")]
    private static partial Regex BreastRegex();

    [GeneratedRegex(@"(\d+)\s*(?:分|min|分钟)")]
    private static partial Regex SleepRegex();

    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*(?:℃|度)")]
    private static partial Regex TempRegex();

    [GeneratedRegex(@"(?:身高|高)\s*(\d+(?:\.\d+)?)\s*(?:cm|厘米)?.*(?:体重|重)\s*(\d+(?:\.\d+)?)\s*(?:kg|公斤|斤)?")]
    private static partial Regex GrowthRegex();

    [GeneratedRegex(@"(\d{1,2})\s*(?:点|:|：)\s*(半|\d{1,2})?")]
    private static partial Regex TimeRegex();
}
