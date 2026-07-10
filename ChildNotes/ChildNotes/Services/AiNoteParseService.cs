using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Services;
using ChildNotes.Shared.Constants;
using ChildNotes.Shared.Dtos;
using ChildNotes.Shared.Services;

namespace ChildNotes.Services;

/// <summary>
/// AI 智能记解析服务：封装"自然语言文本 → 结构化记录"的降级解析逻辑。
/// 抽取自原 AiNote 模态的解析逻辑，供首页快捷输入框共用。
///
/// v2 协议升级：支持一句话解析出多条记录（如"睡了一觉，喝了奶，换了尿布"）。
/// 降级顺序（按 LlmConfig.NoteSource 配置）：
/// - local（默认）：本地 LLM → 规则降级
/// - server：后端接口 → 规则降级
/// </summary>
public sealed class AiNoteParseService
{
    private readonly AiParseApiClient _apiClient = ServiceProvider.Instance.AiParseApiClient;
    private readonly AiAnalysisService _aiService = ServiceProvider.Instance.AiAnalysisService;
    private readonly LlmClient _llmClient = ServiceProvider.Instance.LlmClient;

    /// <summary>本地 LLM 系统提示词：规定输出 JSON 数组（多条记录协议 v2）。</summary>
    internal const string LocalSystemPrompt = """
你是育儿记录解析助手。请将用户输入解析为一条或多条结构化育儿记录，并仅输出 JSON 数组。

若输入包含多个独立事件（如"11点半睡到12点40，吃了130奶粉，喝10ml水"），必须拆分为多条。
若只是一个事件的描述（如"喝了120ml奶"），输出只含一个元素的数组。

支持的 recordType: feed / diaper / sleep / temperature / growth / supplement / water / pump / complementary / abnormal / activity
- feed: 仅限奶类（瓶喂奶粉/母乳、亲喂）；喝水不属于 feed
- feed 子类型: bottle/breast/expressed
- diaper: 换尿布（含大小便相关表述，如"大便/便便/拉屎/拉了/臭臭/粑粑/拉臭/尿尿/嘘嘘"均归此类型）
- diaper 子类型(diaperType): wet/dirty/both/dry
- supplement: 用药/营养补充（维D、益生菌、药品等；不含喝水）；子类型: medicine/nutrition
- water: 喝水（独立类型，amount=水量ml）
- activity 子类型: play/outdoor/exercise

字段：recordType, recordSubType, time(HH:mm), amount(ml数值), duration(分钟),
startTime(sleep专用，开始时间HH:mm), endTime(sleep专用，结束时间HH:mm),
leftDuration, rightDuration, temperature(℃), height(cm), weight(kg), diaperType,
name(supplement专用，药品/营养品名称，不含剂量), dose(supplement专用，剂量数值文本如"0.5"/"1"/"5"),
doseUnit(supplement专用，剂量单位如"包"/"粒"/"ml"/"滴"),
note(备注，supplement 不要把 name/dose 塞进 note), summary(<=30字一句话), confidence(0~1)。

示例输入："11点半睡到12点40，吃了130奶粉，喝10ml水"
示例输出：[{"recordType":"sleep","time":"11:30","startTime":"11:30","endTime":"12:40","duration":70,"summary":"睡眠70分钟","confidence":0.9},
{"recordType":"feed","recordSubType":"bottle","time":"11:30","amount":130,"summary":"瓶喂130ml","confidence":0.9},
{"recordType":"water","time":"11:30","amount":10,"summary":"喝水10ml","confidence":0.8}]

示例输入："拉了大便"
示例输出：[{"recordType":"diaper","diaperType":"dirty","recordSubType":"dirty","note":"大便","summary":"换尿布 大便","confidence":0.9}]

示例输入："吃了半包宝泰康颗粒"
示例输出：[{"recordType":"supplement","recordSubType":"medicine","name":"宝泰康颗粒","dose":"0.5","doseUnit":"包","summary":"用药 宝泰康颗粒 半包","confidence":0.9}]

关键规则：
- "喝奶/吃奶/喂奶" → feed；"喝水/喝10ml水" → water（amount=水量ml）
- "吃药/吃半包XX颗粒" → supplement/medicine（name=药品名，dose=数值如"0.5"，doseUnit=单位如"包"）；"维D/益生菌" → supplement/nutrition
- "大便/便便/拉屎/拉了/臭臭/粑粑/拉臭" → diaper/dirty；"尿尿/嘘嘘/尿了" → diaper/wet；"又尿又拉" → diaper/both
- 时间"11点半"=11:30，"半"在分钟位表示30分

只输出 JSON 数组，不要任何额外文字或 Markdown 代码块。缺失字段用 null。
""";

    /// <summary>
    /// 按 NoteSource 配置选择解析路径并执行三级降级。
    /// 返回多条解析结果列表（至少 1 条；解析完全失败时返回空列表）。
    /// </summary>
    public async Task<List<AiNoteParseItem>> ParseAsync(string text)
    {
        var config = _aiService.GetLlmConfig();
        var preferServer = config.NoteSource == "server";

        // [AI-LOG] 用户输入完整记录：时间戳 + 输入类型 + 具体内容，便于问题分析与行为追踪
        DevLogger.Log("AiNote", $"[AI-LOG] 用户输入 | 时间={DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 类型=NoteParse 路径={(preferServer ? "server" : "local")} 文本={text}");

        DevLogger.Log("AiNote", $"[AI-LOG] 解析路径选择：NoteSource={config.NoteSource ?? "(null)"}, Enabled={config.Enabled}, preferServer={preferServer}");

        // 1) 按用户选择的路径调用 AI
        List<AiNoteParseItem>? aiItems = null;
        if (preferServer)
        {
            aiItems = await TryServerAsync(text);
        }
        else
        {
            aiItems = await TryLocalLlmAsync(text, config);
        }

        if (aiItems is { Count: > 0 })
        {
            // [AI-LOG] 解析结果摘要：记录每条记录的类型/子类型/摘要/来源，便于后续核对
            var summary = string.Join(" | ", aiItems.Select(i => $"{i.RecordType}/{i.RecordSubType ?? "-"}:{i.Summary ?? "(空)"}[src={i.Source}]"));
            DevLogger.Log("AiNote", $"[AI-LOG] 解析成功 {aiItems.Count} 条 | {summary}");
            return aiItems;
        }

        // 2) AI 失败则规则降级
        DevLogger.Log("AiNote", "[AI-LOG] AI 解析未返回结果，降级到规则解析", DevLogger.Level.Warn);
        var ruleItems = LocalRuleParseMulti(text);
        var ruleSummary = string.Join(" | ", ruleItems.Select(i => $"{i.RecordType}/{i.RecordSubType ?? "-"}:{i.Summary ?? "(空)"}[src={i.Source}]"));
        DevLogger.Log("AiNote", $"[AI-LOG] 规则降级解析 {ruleItems.Count} 条 | {ruleSummary}");
        return ruleItems;
    }

    /// <summary>尝试调用后端解析接口；未配置或失败时返回 null。</summary>
    private async Task<List<AiNoteParseItem>?> TryServerAsync(string text)
    {
        var serverUrl = ServiceProvider.Instance.SyncConfigRepository.Get().ServerUrl;
        if (string.IsNullOrEmpty(serverUrl))
        {
            DevLogger.Log("AiNote", "[AI-LOG] 后端解析跳过：ServerUrl 未配置", DevLogger.Level.Warn);
            return null;
        }
        DevLogger.Log("AiNote", $"[AI-LOG] 调用后端解析：{serverUrl}/api/smart-analysis/parse-note");
        try
        {
            var batch = await _apiClient.ParseAsync(text);
            DevLogger.Log("AiNote", $"[AI-LOG] 后端解析返回：{(batch is null ? "null" : $"{batch.Items.Count} 条")}");
            return batch?.Items;
        }
        catch (Exception ex)
        {
            DevLogger.Log("AiNote", "[AI-LOG] 后端解析失败：" + ex.Message, DevLogger.Level.Error);
            return null;
        }
    }

    /// <summary>尝试调用用户配置的本地 LLM；未配置/未启用/失败时返回 null。</summary>
    private async Task<List<AiNoteParseItem>?> TryLocalLlmAsync(string text, LlmConfig config)
    {
        if (config is null || !config.Enabled)
            return null;
        try
        {
            var raw = await _llmClient.ChatAsync(config, LocalSystemPrompt, text);
            var parsed = ParseLlmJsonArray(raw);
            DevLogger.Log("AiNote", $"[AI-LOG] 本地 LLM 解析返回：{(parsed is null ? "null" : $"{parsed.Count} 条")}");
            return parsed;
        }
        catch (Exception ex)
        {
            DevLogger.Log("AiNote", "[AI-LOG] 本地 LLM 解析失败：" + ex.Message, DevLogger.Level.Error);
            return null;
        }
    }

    /// <summary>从 LLM 返回内容中提取 JSON 数组并反序列化为多条记录。</summary>
    private static List<AiNoteParseItem>? ParseLlmJsonArray(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        // 剥离 ```json ... ``` 包裹
        if (s.StartsWith("```"))
        {
            var nl = s.IndexOf('\n');
            if (nl > 0) s = s[(nl + 1)..];
            var last = s.LastIndexOf("```");
            if (last >= 0) s = s[..last];
            s = s.Trim();
        }

        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        // 优先尝试数组解析
        var arrStart = s.IndexOf('[');
        var arrEnd = s.LastIndexOf(']');
        if (arrStart >= 0 && arrEnd > arrStart)
        {
            s = s[arrStart..(arrEnd + 1)];
            try
            {
                var items = JsonSerializer.Deserialize<List<AiNoteParseItem>>(s, opts);
                return items;
            }
            catch { /* 落到单对象分支 */ }
        }

        // 兼容旧版单对象响应：包装成单元素列表
        var objStart = s.IndexOf('{');
        var objEnd = s.LastIndexOf('}');
        if (objStart >= 0 && objEnd > objStart)
        {
            s = s[objStart..(objEnd + 1)];
            try
            {
                var item = JsonSerializer.Deserialize<AiNoteParseItem>(s, opts);
                if (item is not null) return new List<AiNoteParseItem> { item };
            }
            catch { /* 忽略 */ }
        }
        return null;
    }

    // ===== 本地规则降级（多条）=====

    /// <summary>本地规则降级多条解析：先用 NoteSplitter 切分，再逐段解析。</summary>
    public static List<AiNoteParseItem> LocalRuleParseMulti(string text)
    {
        var segments = NoteSplitter.Split(text);
        if (segments.Count == 0)
            return new List<AiNoteParseItem> { LocalRuleParse(text) };

        var results = new List<AiNoteParseItem>(segments.Count);
        foreach (var seg in segments)
        {
            var item = LocalRuleParse(seg);
            // 兜底未识别（activity + 原文）的段不加入结果，避免噪声
            // 但如果整句只有一段且未识别，仍需返回兜底
            if (item.RecordType == RecordType.Activity && item.Confidence <= 0.2 && segments.Count > 1)
                continue;
            results.Add(item);
        }
        if (results.Count == 0)
            results.Add(LocalRuleParse(text));
        return results;
    }

    /// <summary>本地规则降级解析：覆盖最常见的育儿记录表述。</summary>
    public static AiNoteParseItem LocalRuleParse(string text)
    {
        // 0) supplement（用药/营养）：必须在 feed 之前判定
        if (IsSupplementLike(text))
        {
            return ParseSupplement(text);
        }

        // 1) 喂奶：奶量
        var m = Regex.Match(text, @"(\d+)\s*(?:ml|毫升|mL)");
        if (m.Success && (text.Contains("奶") || text.Contains("喂") || text.Contains("吃")))
        {
            return new AiNoteParseItem
            {
                RecordType = RecordType.Feed,
                RecordSubType = FeedType.Bottle,
                Amount = int.TryParse(m.Groups[1].Value, out var a) ? a : null,
                Time = ExtractTime(text),
                Summary = "瓶喂 " + m.Groups[1].Value + "ml",
                Confidence = 0.4,
                Source = ParseSource.Rule,
            };
        }

        // 亲喂（右单位可选，兼容"左10右15分"省略中间单位的写法）
        var bm = Regex.Match(text, @"(?:左|left)\s*(\d+)\s*(?:分|min|分钟)?.*?(?:右|right)\s*(\d+)\s*(?:分|min|分钟)?");
        if (bm.Success)
        {
            var l = int.TryParse(bm.Groups[1].Value, out var lv) ? lv : 0;
            var r = int.TryParse(bm.Groups[2].Value, out var rv) ? rv : 0;
            return new AiNoteParseItem
            {
                RecordType = RecordType.Feed,
                RecordSubType = FeedType.Breast,
                LeftDuration = l,
                RightDuration = r,
                Time = ExtractTime(text),
                Summary = $"亲喂 左{l} 右{r}分钟",
                Confidence = 0.4,
                Source = ParseSource.Rule,
            };
        }

        // 2) 换尿布（含大便/小便相关各种口语表述）
        if (text.Contains("尿布") || text.Contains("换尿") || text.Contains("嘘嘘") || text.Contains("便便") || text.Contains("拉屎") || text.Contains("拉尿")
            || text.Contains("又尿又拉")
            || text.Contains("大便") || text.Contains("小便") || text.Contains("拉了") || text.Contains("臭臭")
            || text.Contains("粑粑") || text.Contains("拉臭") || text.Contains("尿尿")
            || Regex.IsMatch(text, @"(^|[^布])尿了") || Regex.IsMatch(text, @"(^|[^布])便了"))
        {
            if (text.Contains("干爽") || text.Contains("干燥"))
            {
                return new AiNoteParseItem
                {
                    RecordType = RecordType.Diaper,
                    DiaperType = DiaperType.Dry,
                    RecordSubType = DiaperType.Dry,
                    Time = ExtractTime(text),
                    Summary = "换尿布 干爽",
                    Confidence = 0.4,
                    Source = ParseSource.Rule,
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
                DiaperType = sub,
                RecordSubType = sub,
                Time = ExtractTime(text),
                Summary = "换尿布 " + sub,
                Confidence = 0.4,
                Source = ParseSource.Rule,
            };
        }

        // 3) 睡眠
        var sm = Regex.Match(text, @"(\d+)\s*(?:分|min|分钟)");
        var hasSleepKw = text.Contains("睡") || text.Contains("入睡") || text.Contains("小睡") || text.Contains("睡到");
        // 提取"X睡到Y"格式的起止时间
        var (sleepStart, sleepEnd) = ExtractSleepRange(text);

        if (sm.Success && hasSleepKw)
        {
            var st = sleepStart ?? ExtractTime(text);
            return new AiNoteParseItem
            {
                RecordType = RecordType.Sleep,
                Duration = int.TryParse(sm.Groups[1].Value, out var d) ? d : null,
                Time = st,
                StartTime = st,
                EndTime = sleepEnd,
                Summary = sm.Groups[1].Value + "分钟睡眠",
                Confidence = 0.4,
                Source = ParseSource.Rule,
            };
        }
        // "X点Y睡到A点B" 格式：计算时长
        if (!sm.Success && text.Contains("睡到"))
        {
            var dur = TryCalcSleepDuration(text);
            if (dur.HasValue)
            {
                var st = sleepStart ?? ExtractTime(text);
                return new AiNoteParseItem
                {
                    RecordType = RecordType.Sleep,
                    Duration = dur,
                    Time = st,
                    StartTime = st,
                    EndTime = sleepEnd,
                    Summary = dur + "分钟睡眠",
                    Confidence = 0.5,
                    Source = ParseSource.Rule,
                };
            }
        }
        if (hasSleepKw)
        {
            var st = sleepStart ?? ExtractTime(text);
            return new AiNoteParseItem
            {
                RecordType = RecordType.Sleep,
                Time = st,
                StartTime = st,
                EndTime = sleepEnd,
                Summary = "睡眠",
                Confidence = 0.35,
                Source = ParseSource.Rule,
            };
        }

        // 4) 体温
        var tm = Regex.Match(text, @"(\d+(?:\.\d+)?)\s*(?:℃|度)");
        if (tm.Success && (text.Contains("体温") || text.Contains("烧")))
        {
            return new AiNoteParseItem
            {
                RecordType = RecordType.Temperature,
                Temperature = decimal.TryParse(tm.Groups[1].Value, out var t) ? t : null,
                Time = ExtractTime(text),
                Summary = "体温 " + tm.Groups[1].Value + "℃",
                Confidence = 0.4,
                Source = ParseSource.Rule,
            };
        }

        // 5) 身高体重
        var gm = Regex.Match(text, @"(?:身高|高)\s*(\d+(?:\.\d+)?)\s*(?:cm|厘米)?.*(?:体重|重)\s*(\d+(?:\.\d+)?)\s*(?:kg|公斤|斤)?");
        if (gm.Success)
        {
            return new AiNoteParseItem
            {
                RecordType = RecordType.Growth,
                Height = decimal.TryParse(gm.Groups[1].Value, out var h) ? h : null,
                Weight = decimal.TryParse(gm.Groups[2].Value, out var w) ? w : null,
                Time = ExtractTime(text),
                Summary = "身高 " + gm.Groups[1].Value + "cm 体重 " + gm.Groups[2].Value + "kg",
                Confidence = 0.35,
                Source = ParseSource.Rule,
            };
        }

        // 兜底
        return new AiNoteParseItem
        {
            RecordType = RecordType.Activity,
            Note = text,
            Time = ExtractTime(text),
            Summary = "未识别记录",
            Confidence = 0.2,
            Source = ParseSource.Rule,
        };
    }

    // ===== Supplement（用药/营养）分支 =====

    private static bool IsSupplementLike(string text)
    {
        // 剂型关键词
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

    private static AiNoteParseItem ParseSupplement(string text)
    {
        // 子类型：medicine / nutrition
        bool isMedicine = text.Contains("颗粒") || text.Contains("冲剂") || text.Contains("糖浆")
            || text.Contains("滴剂") || text.Contains("药片") || text.Contains("胶囊")
            || text.Contains("药丸") || text.Contains("吃药") || text.Contains("服药")
            || text.Contains("保泰康") || text.Contains("泰诺") || text.Contains("美林")
            || text.Contains("药");
        string subType = isMedicine ? "medicine" : "nutrition";

        // 剂量：包/粒/滴/ml
        int? amount = null;
        var doseMatch = Regex.Match(text, @"(\d+)\s*(?:ml|毫升|mL|包|粒|滴|片|丸)");
        if (doseMatch.Success)
            amount = int.TryParse(doseMatch.Groups[1].Value, out var a) ? a : null;
        // "半包" → 0.5 包（amount 用整数近似为 1，note 保留原文）
        if (!amount.HasValue && text.Contains("半"))
            amount = 1;

        // 名称提取
        var name = ExtractSupplementName(text);

        return new AiNoteParseItem
        {
            RecordType = RecordType.Supplement,
            RecordSubType = subType,
            Amount = amount,
            Note = name ?? text,
            Time = ExtractTime(text),
            Summary = (isMedicine ? "用药 " : "营养 ") + (name ?? text),
            Confidence = 0.5,
            Source = ParseSource.Rule,
        };
    }

    /// <summary>从文本中提取药品/营养品名称（去掉时段词、时间、剂量、动词等噪声词）。</summary>
    private static string? ExtractSupplementName(string text)
    {
        var s = text;
        // 去掉时段词（"早上/早晨/上午/中午/下午/傍晚/晚上/夜里/夜间/半夜"）
        s = Regex.Replace(s, @"早上|早晨|上午|中午|下午|傍晚|晚上|夜里|夜间|半夜|今早|今晚|昨日|明天", "");
        // 去掉时间前缀
        s = Regex.Replace(s, @"(\d{1,2})\s*(?:点|:|：)\s*(半|\d{1,2})?\s*", "");
        // 去掉动词
        s = Regex.Replace(s, @"(?:喝了|喝了|喝|吃了|吃|服用|服)", "");
        // 去掉剂量
        s = Regex.Replace(s, @"(\d+)?\s*(?:ml|毫升|mL|包|粒|滴|片|丸)", "");
        // "半包"等
        s = Regex.Replace(s, @"半\s*(?:包|粒|滴|片|丸)", "");
        s = s.Trim(' ', '，', ',', '。');
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    /// <summary>计算"X点Y睡到A点B"格式睡眠时长（分钟）。支持 12 小时制跨午/跨日。</summary>
    private static int? TryCalcSleepDuration(string text)
    {
        var m = Regex.Match(text, @"(\d{1,2})\s*(?:点|:|：)\s*(半|\d{0,2})\s*睡到\s*(\d{1,2})\s*(?:点|:|：)\s*(半|\d{0,2})");
        if (!m.Success) return null;

        int startH = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        int startM = ParseMinuteGroup(m.Groups[2]);
        int endH = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        int endM = ParseMinuteGroup(m.Groups[4]);

        int start = startH * 60 + startM;
        int end = endH * 60 + endM;
        // 跨日处理：若结束早于开始，加 24 小时
        if (end < start) end += 24 * 60;

        return end - start;
    }

    /// <summary>从"X睡到Y"格式提取睡眠起止时间，返回 ("HH:mm", "HH:mm")；无法提取则返回 (null, null)。</summary>
    private static (string? Start, string? End) ExtractSleepRange(string text)
    {
        var m = Regex.Match(text, @"(\d{1,2})\s*(?:点|:|：)\s*(半|\d{0,2})\s*睡到\s*(\d{1,2})\s*(?:点|:|：)\s*(半|\d{0,2})");
        if (!m.Success) return (null, null);
        if (!int.TryParse(m.Groups[1].Value, out var sh)) return (null, null);
        var sm = ParseMinuteGroup(m.Groups[2]);
        if (!int.TryParse(m.Groups[3].Value, out var eh)) return (null, null);
        var em = ParseMinuteGroup(m.Groups[4]);
        if (sh < 0 || sh > 23 || sm < 0 || sm > 59) return (null, null);
        if (eh < 0 || eh > 23 || em < 0 || em > 59) return (null, null);
        return ($"{sh:D2}:{sm:D2}", $"{eh:D2}:{em:D2}");
    }

    /// <summary>解析分钟分组："半"=30, 空=0, 数字=本身。</summary>
    private static int ParseMinuteGroup(System.Text.RegularExpressions.Group g)
    {
        var v = g.Value;
        if (string.IsNullOrEmpty(v)) return 0;
        if (v == "半") return 30;
        return int.TryParse(v, out var n) ? n : 0;
    }

    // ===== 时间解析 =====

    private static string? ExtractTime(string text)
    {
        var m = Regex.Match(text, @"(\d{1,2})\s*(?:点|:|：)\s*(半|\d{1,2})?");
        if (m.Success)
        {
            var hh = int.TryParse(m.Groups[1].Value, out var h) ? h : -1;
            var mm = ParseMinuteGroup(m.Groups[2]);
            if (hh < 0 || hh > 23 || mm < 0 || mm > 59) return null;
            if (hh < 12 && (text.Contains("晚上") || text.Contains("下午") || text.Contains("傍晚") || text.Contains("夜里")))
                hh += 12;
            return $"{hh:D2}:{mm:D2}";
        }
        return null;
    }

    // ===== 保存到本地数据库 =====

    /// <summary>将解析结果按现有数据分类标准存储到本地数据库。</summary>
    public static void SaveLocally(AiNoteParseItem r, string originalText, RecordService recordService)
    {
        var time = string.IsNullOrEmpty(r.Time) ? DateTime.Now.ToString("O") : NormalizeTime(r.Time);
        switch (r.RecordType)
        {
            case RecordType.Feed:
                if (r.RecordSubType == FeedType.Breast)
                {
                    recordService.AddFeed(new ChildNotes.Shared.Dtos.FeedRecordDto
                    {
                        Type = FeedType.Breast,
                        Time = time,
                        LeftDuration = r.LeftDuration,
                        RightDuration = r.RightDuration,
                        LeftDurationSec = (r.LeftDuration ?? 0) * 60,
                        RightDurationSec = (r.RightDuration ?? 0) * 60,
                        Note = r.Note,
                    });
                }
                else
                {
                    recordService.AddFeed(new ChildNotes.Shared.Dtos.FeedRecordDto
                    {
                        Type = string.IsNullOrEmpty(r.RecordSubType) ? FeedType.Bottle : r.RecordSubType,
                        Time = time,
                        Amount = r.Amount,
                        Note = r.Note,
                    });
                }
                break;
            case RecordType.Diaper:
                recordService.AddDiaper(new ChildNotes.Shared.Dtos.DiaperRecordDto
                {
                    Type = string.IsNullOrEmpty(r.DiaperType) ? (r.RecordSubType ?? DiaperType.Dry) : r.DiaperType,
                    Time = time,
                });
                break;
            case RecordType.Sleep:
                recordService.AddSleep(new ChildNotes.Shared.Dtos.SleepRecordDto
                {
                    Time = time,
                    StartTime = string.IsNullOrEmpty(r.StartTime) ? time : CombineDateAndTime(time, r.StartTime),
                    EndTime = string.IsNullOrEmpty(r.EndTime) ? null : CombineDateAndTime(time, r.EndTime),
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
                    // 名称：优先用 AI/规则返回的 Name，缺失时回退到 Note，最后回退到"AI 识别"
                    Name = r.Name ?? r.Note ?? "AI 识别",
                    // 剂量：优先用结构化 Dose（数值文本），其次用 Amount(ml) 构造
                    Dose = r.Dose ?? (r.Amount.HasValue ? r.Amount.Value.ToString() : null),
                    // 单位：优先用 AI 返回的 DoseUnit；Amount 回退时单位为 ml
                    DoseUnit = r.DoseUnit ?? (r.Amount.HasValue ? "ml" : null),
                    Time = time,
                });
                break;
            case RecordType.Water:
                recordService.AddWater(new ChildNotes.Shared.Dtos.WaterRecordDto
                {
                    AmountMl = r.Amount,
                    Note = r.Note,
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
        if (Regex.IsMatch(time, @"^\d{1,2}:\d{2}$"))
        {
            return DateTime.Today.Add(TimeSpan.Parse(time)).ToString("O");
        }
        return DateTime.Now.ToString("O");
    }

    /// <summary>将完整时间（ISO "O" 格式）的日期部分与 "HH:mm" 拼接，支持跨日（endTime &lt; startTime 时日期 +1）。</summary>
    private static string CombineDateAndTime(string fullTimeIso, string hhMm)
    {
        if (!DateTime.TryParse(fullTimeIso, out var baseDt)) return fullTimeIso;
        if (!TimeSpan.TryParse(hhMm, out var t)) return fullTimeIso;
        var result = baseDt.Date.Add(t);
        // 若结束时间小于开始时间，说明跨午夜，日期 +1
        if (t < baseDt.TimeOfDay) result = result.AddDays(1);
        return result.ToString("O");
    }

    // ===== Toast 显示格式化 =====

    /// <summary>
    /// 将解析结果格式化为 Toast 单行紧凑文本，对齐喂养记录卡片信息密度。
    /// 格式示例：🍼 瓶喂 130ml (11:30) / 😴 睡眠 70分钟 (11:30) / 💊 用药 宝泰康颗粒 半包
    /// </summary>
    public static string FormatForToast(AiNoteParseItem r)
    {
        var time = string.IsNullOrEmpty(r.Time) ? "" : $" ({r.Time})";
        return r.RecordType switch
        {
            RecordType.Feed => r.RecordSubType == FeedType.Breast
                ? $"🍼 母乳亲喂 左{r.LeftDuration ?? 0} 右{r.RightDuration ?? 0}分钟{time}"
                : $"🍼 瓶喂{(r.RecordSubType == FeedType.Expressed ? "(母乳)" : "")} {r.Amount ?? 0}ml{time}",
            RecordType.Diaper => $"💩 {DiaperText(r.DiaperType ?? r.RecordSubType)}{time}",
            RecordType.Sleep => r.Duration.HasValue
                ? $"😴 睡眠 {r.Duration}分钟{time}"
                : $"😴 睡眠{time}",
            RecordType.Temperature => $"🌡️ 体温 {(r.Temperature ?? 0):F1}℃{time}",
            RecordType.Growth => $"📏 {FormatGrowth(r)}{time}",
            RecordType.Supplement => $"💊 {SupplementText(r)}{time}",
            RecordType.Water => $"💧 喝水 {r.Amount ?? 0}ml{time}",
            RecordType.Pump => $"🥛 吸奶 {r.Amount ?? 0}ml{time}",
            RecordType.Complementary => $"🥣 辅食{(string.IsNullOrEmpty(r.Note) ? "" : " " + r.Note)}{time}",
            RecordType.Abnormal => $"⚠️ 异常{(string.IsNullOrEmpty(r.Note) ? "" : " " + r.Note)}{time}",
            RecordType.Activity => $"🏃 活动{(string.IsNullOrEmpty(r.Note) ? "" : " " + r.Note)}{time}",
            _ => $"📝 {r.Summary ?? "已记录"}{time}",
        };
    }

    private static string DiaperText(string? sub) => sub switch
    {
        "wet" => "小便",
        "dirty" => "大便",
        "both" => "大小便",
        "dry" => "换尿布 干爽",
        _ => "换尿布",
    };

    private static string FormatGrowth(AiNoteParseItem r)
    {
        var parts = new List<string>();
        if (r.Height.HasValue) parts.Add($"身高{r.Height}cm");
        if (r.Weight.HasValue) parts.Add($"体重{r.Weight}kg");
        return parts.Count > 0 ? string.Join(" ", parts) : "成长记录";
    }

    private static string SupplementText(AiNoteParseItem r)
    {
        var type = r.RecordSubType == "medicine" ? "用药" : "补充剂";
        var name = r.Name ?? r.Note ?? "";
        var dose = FormatDose(r.Dose, r.DoseUnit);
        var parts = new List<string> { type };
        if (!string.IsNullOrEmpty(name)) parts.Add(name);
        if (!string.IsNullOrEmpty(dose)) parts.Add(dose);
        return string.Join(" ", parts);
    }

    private static string FormatDose(string? dose, string? unit)
    {
        if (string.IsNullOrEmpty(dose)) return "";
        if (string.IsNullOrEmpty(unit)) return dose;
        if (dose == "0.5") return "半" + unit;
        return dose + unit;
    }
}
