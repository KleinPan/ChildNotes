using System.Text.Json;
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
///
/// 规则降级逻辑已提取到 ChildNotes.Shared/Services/AiNoteRuleParser，前后端共用。
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

字段：recordType, recordSubType, time, amount(ml数值), duration(分钟),
startTime(sleep/activity专用，开始时间), endTime(sleep/activity专用，结束时间),
leftDuration, rightDuration, temperature(℃), height(cm), weight(kg), diaperType,
name(supplement专用，药品/营养品名称，不含剂量), dose(supplement专用，剂量数值文本如"0.5"/"1"/"5"),
doseUnit(supplement专用，剂量单位如"包"/"粒"/"ml"/"滴"),
foodName(complementary专用，食物名称如"南瓜泥"/"蛋黄"), foodTypes(complementary专用，食材类型数组如["蔬菜"]),
amountText(complementary专用，食量数值如"20"), amountUnit(complementary专用，食量单位如"克"/"个"/"勺"/"碗"),
note(备注，仅用于真正的补充说明，如"吃后半段哭闹"、"温度计换了电池"), summary(<=30字一句话), confidence(0~1)。

activity 字段使用规则（重要）：
- activity 有明确起止时间（如"10点到11点做游戏"）→ 填 startTime/endTime（"HH:mm"），duration 由起止差值算出
- activity 只有时长（如"玩了30分钟"）→ 填 duration，startTime/endTime 留 null
- activity 有起止时间时，time = startTime；仅有时长时，time = 活动开始时间（若未提及则 null）

note 字段使用规则（重要，避免备注与结构化字段重复）：
- note 仅用于真正的补充说明，不要把已结构化字段的内容原文写入 note
- amount/dose/name/foodName/duration 等已结构化的字段，其值不要重复写入 note
- 模糊量词处理："110多奶粉"→amount=110（不要 note="110多奶粉"）；"约120ml奶"→amount=120（不要 note）；"大概30分钟"→duration=30（不要 note）
- 仅当用户有真正的备注信息（如"吃完吐了一点"、"左边的奶冲多了倒掉"）时才填 note，否则 note=null

时间格式规则（重要）：
- time/startTime/endTime 默认用 "HH:mm" 格式（如 "20:05"、"08:30"）
- 若用户提到"昨晚/昨天/前天/大前天"等相对日期词，必须用完整格式 "yyyy-MM-dd HH:mm"（基于当前日期偏移）
  示例：今天 2026-07-11，用户说"昨晚8:05" → time="2026-07-10 20:05"
  示例：今天 2026-07-11，用户说"前天下午3点" → time="2026-07-09 15:00"
- 当前日期时间：{NowText}

示例输入："11点半睡到12点40，吃了130奶粉，喝10ml水"
示例输出：[{"recordType":"sleep","time":"11:30","startTime":"11:30","endTime":"12:40","duration":70,"summary":"睡眠70分钟","confidence":0.9},
{"recordType":"feed","recordSubType":"bottle","time":"11:30","amount":130,"summary":"瓶喂130ml","confidence":0.9},
{"recordType":"water","time":"11:30","amount":10,"summary":"喝水10ml","confidence":0.8}]

示例输入："吃了南瓜泥20克"
示例输出：[{"recordType":"complementary","foodName":"南瓜泥","foodTypes":["蔬菜"],"amountText":"20","amountUnit":"克","summary":"辅食 南瓜泥 20克","confidence":0.9}]

示例输入："拉了大便"
示例输出：[{"recordType":"diaper","diaperType":"dirty","recordSubType":"dirty","note":"大便","summary":"换尿布 大便","confidence":0.9}]

示例输入："吃了半包宝泰康颗粒"
示例输出：[{"recordType":"supplement","recordSubType":"medicine","name":"宝泰康颗粒","dose":"0.5","doseUnit":"包","summary":"用药 宝泰康颗粒 半包","confidence":0.9}]

示例输入："2:20喝了110多奶粉和10ml水"
示例输出：[{"recordType":"feed","recordSubType":"bottle","time":"02:20","amount":110,"note":null,"summary":"瓶喂110ml","confidence":0.9},
{"recordType":"water","time":"02:20","amount":10,"note":null,"summary":"喝水10ml","confidence":0.8}]

示例输入："10点到11点做游戏"
示例输出：[{"recordType":"activity","recordSubType":"play","time":"10:00","startTime":"10:00","endTime":"11:00","duration":60,"summary":"游戏 10:00-11:00","confidence":0.9}]

示例输入："户外散步30分钟"
示例输出：[{"recordType":"activity","recordSubType":"outdoor","duration":30,"summary":"户外 30分钟","confidence":0.9}]

关键规则：
- "喝奶/吃奶/喂奶" → feed；"喝水/喝10ml水" → water（amount=水量ml）
- "吃药/吃半包XX颗粒" → supplement/medicine（name=药品名，dose=数值如"0.5"，doseUnit=单位如"包"）；"维D/益生菌" → supplement/nutrition
- "大便/便便/拉屎/拉了/臭臭/粑粑/拉臭" → diaper/dirty；"尿尿/嘘嘘/尿了" → diaper/wet；"又尿又拉" → diaper/both
- 时间"11点半"=11:30，"半"在分钟位表示30分
- 12 小时制时间解析（重要，取最近的过去时刻）：
  - 用户显式说"上午/早上/凌晨"等 AM 时段词 → 12 小时制时间按 AM 解析（如"早上5点"=05:00）
  - 用户显式说"下午/晚上/傍晚/夜里"等 PM 时段词 → 1~11 点 +12 转 24 小时制（如"下午5点"=17:00、"晚上8点"=20:00）
  - 用户未说时段词时（如"5点吃了奶"、"2点喝奶粉"）→ 取最近的过去时刻：
    * 计算 AM 候选（如 05:00）和 PM 候选（如 17:00），选择 <= 当前时间且最接近当前时间的那个
    * 若两个候选都已过去，选 PM（更近）；若都未到，选 AM（更早，假设刚发生）
  - 示例（当前 08:49 时）："2点喝了奶粉" → time="02:00"（AM 候选已过去，PM 候选未到，取 AM）
  - 示例（当前 15:00 时）："2点吃了奶" → time="14:00"（AM 候选已过去13小时，PM 候选已过去1小时，取更近的 PM）
  - 示例："早上5点吃了奶" → time="05:00"（显式"早上"，强制 AM）
  - 示例："晚上8点睡了一觉" → time="20:00"（显式"晚上"，强制 PM）

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

        // 2) AI 失败则规则降级（调用共享层 AiNoteRuleParser）
        DevLogger.Log("AiNote", "[AI-LOG] AI 解析未返回结果，降级到规则解析", DevLogger.Level.Warn);
        var ruleItems = AiNoteRuleParser.ParseMulti(text);
        var ruleSummary = string.Join(" | ", ruleItems.Select(i => $"{i.RecordType}/{i.RecordSubType ?? "-"}:{i.Summary ?? "(空)"}[src={i.Source}]"));
        DevLogger.Log("AiNote", $"[AI-LOG] 规则降级解析 {ruleItems.Count} 条 | {ruleSummary}");
        return ruleItems;
    }

    /// <summary>
    /// 尝试调用后端解析接口；未配置或一般失败时返回 null（降级到本地 LLM/规则）。
    /// AI 记次数用尽（AI_NOTE_LIMIT_EXCEEDED）时抛出 <see cref="AiNoteApiException"/>，
    /// 不降级，由上层提示用户升级会员。
    /// </summary>
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
            var batch = await _apiClient.ParseWithErrorsAsync(text);
            DevLogger.Log("AiNote", $"[AI-LOG] 后端解析返回：{batch.Items.Count} 条");
            return batch.Items;
        }
        catch (AiNoteApiException ex)
        {
            // AI 次数用尽：不降级，向上抛出由 ViewModel 引导用户升级会员
            if (ex.IsAiNoteLimitExceeded)
            {
                DevLogger.Log("AiNote", "[AI-LOG] AI 记次数已用尽，不降级", DevLogger.Level.Warn);
                throw;
            }
            // 其他业务错误（如文本为空等）：降级到本地
            DevLogger.Log("AiNote", "[AI-LOG] 后端解析业务失败：" + ex.Message, DevLogger.Level.Warn);
            return null;
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
            var raw = await _llmClient.ChatAsync(config, LocalSystemPrompt.Replace("{NowText}", DateTime.Now.ToString("yyyy-MM-dd HH:mm")), text);
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

    // ===== 保存到本地数据库 =====

    /// <summary>将解析结果按现有数据分类标准存储到本地数据库。</summary>
    public static void SaveLocally(AiNoteParseItem r, string originalText, RecordService recordService)
    {
        var time = string.IsNullOrEmpty(r.Time) ? DateTime.Now.ToString("O") : AiNoteRuleParser.NormalizeTime(r.Time);
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
                    StartTime = string.IsNullOrEmpty(r.StartTime) ? time : AiNoteRuleParser.CombineDateAndTime(time, r.StartTime),
                    EndTime = string.IsNullOrEmpty(r.EndTime) ? null : AiNoteRuleParser.CombineDateAndTime(time, r.EndTime),
                    Duration = r.Duration,
                });
                break;
            case RecordType.Temperature:
                recordService.AddTemperature(new ChildNotes.Shared.Dtos.TemperatureRecordDto
                {
                    Temperature = r.Temperature ?? 0,
                    IsAbnormal = (r.Temperature ?? 0) >= HealthConstants.FeverThreshold,
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
                    FoodName = r.FoodName ?? r.Note,
                    FoodTypes = r.FoodTypes ?? new List<string>(),
                    Amount = r.AmountText,
                    AmountUnit = r.AmountUnit,
                    Note = r.Note,
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
                    // 有起止时间则存 EndTime（与表单编辑路径一致，时间轴显示"开始→结束"）
                    EndTime = string.IsNullOrEmpty(r.EndTime) ? null : AiNoteRuleParser.CombineDateAndTime(time, r.EndTime),
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
            RecordType.Complementary => $"🥣 辅食 {(string.IsNullOrEmpty(r.FoodName) ? "" : r.FoodName)}{(string.IsNullOrEmpty(r.AmountText) ? "" : $" {r.AmountText}{r.AmountUnit ?? ""}")}{time}",
            RecordType.Abnormal => $"⚠️ 异常{(string.IsNullOrEmpty(r.Note) ? "" : " " + r.Note)}{time}",
            RecordType.Activity => r.EndTime is not null && r.StartTime is not null
                ? $"🏃 活动{(string.IsNullOrEmpty(r.Note) ? "" : " " + r.Note)} {r.StartTime}-{r.EndTime}{time}"
                : r.Duration.HasValue
                    ? $"🏃 活动{(string.IsNullOrEmpty(r.Note) ? "" : " " + r.Note)} {r.Duration}分钟{time}"
                    : $"🏃 活动{(string.IsNullOrEmpty(r.Note) ? "" : " " + r.Note)}{time}",
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
