using System.Diagnostics;
using System.Text.Json;
using ChildNotes.Core.Constants;
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
///
/// 次数限制：非会员 10 次/天，会员 100 次/天（按自然日重置）。
/// 规则降级解析同样消耗次数（保证后端 AI 调用配额可控）。
/// 规则降级逻辑已提取到 ChildNotes.Shared/Services/AiNoteRuleParser，前后端共用。
/// </summary>
public partial class AiNoteService : IAiNoteService
{
    private readonly ILogger<AiNoteService> _logger;
    private readonly DeepSeekClient _ai;
    private readonly IMembershipService _membership;
    private readonly ICurrentUserService _current;

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
- time: 时间，默认 "HH:mm" 格式；若用户提到"昨晚/昨天/前天/大前天"等相对日期词，必须用完整 "yyyy-MM-dd HH:mm" 格式（基于当前日期偏移）。未提及时间则使用 null（后端将取当前时间）
- amount: 数值型，单位 ml（用于 feed 瓶喂、pump 总量等）
- duration: 数值型，单位分钟（用于 sleep、activity）
- startTime: sleep/activity 专用，开始时间，格式同 time；若只提到一个时间点则同时填 startTime 和 time
- endTime: sleep/activity 专用，结束时间，格式同 time；有明确结束时间时填写
- leftDuration / rightDuration: 数值型，分钟（用于 feed 亲喂、pump）
- temperature: 数值型，℃
- height: 数值型，cm
- weight: 数值型，kg
- diaperType: wet/dirty/both/dry（仅 diaper 类型使用，与 recordSubType 同义，二者任填其一）
- name: supplement 专用，药品/营养品名称（如"宝泰康颗粒"、"伊可新"、"维D"），不含剂量
- dose: supplement 专用，剂量数值文本（如"0.5"、"1"、"5"），不含单位
- doseUnit: supplement 专用，剂量单位（如"包"、"粒"、"ml"、"滴"），与 dose 分开
- foodName: complementary 专用，食物名称（如"南瓜泥"、"蛋黄"、"米粉"）
- foodTypes: complementary 专用，食材类型数组（如["蔬菜","水果","主食","肉蛋"]）
- amountText: complementary 专用，食量数值文本（如"20"、"半碗"）
- amountUnit: complementary 专用，食量单位（如"克"、"个"、"勺"、"碗"），与 amountText 分开
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
  {"recordType":"sleep","time":"23:30","startTime":"23:30","endTime":"00:40","duration":70,"summary":"睡眠70分钟","confidence":0.9},
  {"recordType":"feed","recordSubType":"bottle","amount":130,"time":null,"summary":"瓶喂130ml","confidence":0.9},
  {"recordType":"water","amount":10,"time":null,"summary":"喝水10ml","confidence":0.8}
]

输入"拉了大便"应输出：
[{"recordType":"diaper","diaperType":"dirty","recordSubType":"dirty","note":"大便","summary":"换尿布 大便","confidence":0.9}]

输入"换尿布 便便"应输出：
[{"recordType":"diaper","diaperType":"dirty","recordSubType":"dirty","summary":"换尿布 便便","confidence":0.9}]

输入"吃了半包宝泰康颗粒"应输出：
[{"recordType":"supplement","recordSubType":"medicine","name":"宝泰康颗粒","dose":"0.5","doseUnit":"包","summary":"用药 宝泰康颗粒 半包","confidence":0.9}]

输入"吃了南瓜泥20克"应输出：
[{"recordType":"complementary","foodName":"南瓜泥","foodTypes":["蔬菜"],"amountText":"20","amountUnit":"克","summary":"辅食 南瓜泥 20克","confidence":0.9}]

输入"10点到11点做游戏"应输出：
[{"recordType":"activity","recordSubType":"play","time":"10:00","startTime":"10:00","endTime":"11:00","duration":60,"summary":"游戏 10:00-11:00","confidence":0.9}]

输入"户外散步30分钟"应输出：
[{"recordType":"activity","recordSubType":"outdoor","duration":30,"summary":"户外 30分钟","confidence":0.9}]

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
    * 当前时间见上方"当前日期时间"
  - 示例（当前 08:49 时）："2点喝了奶粉" → time="02:00"（AM 候选 02:00 已过去6小时，PM 候选 14:00 未到，取已过去的 AM）
  - 示例（当前 15:00 时）："2点吃了奶" → time="14:00"（AM 候选 02:00 已过去13小时，PM 候选 14:00 已过去1小时，取更近的 PM）
  - 示例（当前 09:00 时）："5点吃了奶" → time="05:00"（AM 候选已过去，PM 候选未到，取 AM）
  - 示例（任意时间）："早上5点吃了奶" → time="05:00"（显式"早上"，强制 AM）
  - 示例（任意时间）："晚上8点睡了一觉" → time="20:00"（显式"晚上"，强制 PM）
- 相对日期词：含"昨晚/昨天/昨夜"则日期为今天-1；"前天"为今天-2；"大前天"为今天-3。此时 time 必须用 "yyyy-MM-dd HH:mm" 完整格式
  示例：若今天 2026-07-11，用户说"昨晚8:05吃了50ml奶粉" → time="2026-07-10 20:05"
  示例：若今天 2026-07-11，用户说"前天下午3点睡觉" → time="2026-07-09 15:00"
- 当前日期时间：{NowText}
""";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public AiNoteService(DeepSeekClient ai, ILogger<AiNoteService> logger, IMembershipService membership, ICurrentUserService current)
    {
        _ai = ai;
        _logger = logger;
        _membership = membership;
        _current = current;
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

        // 每日次数限制检查（非会员 10 次/天，会员 100 次/天）
        var uid = _current.RequireUserId();
        var limit = await _membership.GetAiNoteDailyLimitAsync(uid, ct);
        var used = await _membership.GetAiNoteUsedTodayAsync(uid, ct);
        if (used >= limit)
            throw new BusinessException($"今日 AI 记次数已用完（{used}/{limit}），升级会员可获得更多次数", 400, "AI_NOTE_LIMIT_EXCEEDED");

        List<AiNoteParseItem> items;
        try
        {
            items = await ParseByAiAsync(text, ct);
        }
        catch (Exception ex)
        {
            // 降级到规则解析：保证可用性，置信度下调
            _logger.LogWarning(ex, "[AI-LOG] AI 解析失败，降级到规则兜底。Text={Text}", text);
            items = AiNoteRuleParser.ParseMulti(text);
            foreach (var it in items)
            {
                it.Confidence = 0.4;
                it.Source = ParseSource.Rule;
            }
        }

        // 时间未提供则使用当前时间（对每个 Item 应用）
        // 并对 LLM 返回的 12 小时制无时段词时间做"取最近过去时刻"后处理
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        foreach (var it in items)
        {
            if (string.IsNullOrEmpty(it.Time))
            {
                it.Time = now;
            }
            else
            {
                // 先归一化，再对无时段词的 12 小时制时间做后处理
                var normalized = AiNoteRuleParser.NormalizeTime(it.Time, "yyyy-MM-dd HH:mm");
                it.Time = AiNoteRuleParser.NormalizeAmbiguousTime(normalized, text);
            }
        }

        _logger.LogInformation("[AI-LOG] 解析完成 Items={Count} FirstType={FirstType} FirstSubType={FirstSubType} Text={Text}",
            items.Count,
            items.FirstOrDefault()?.RecordType ?? "-",
            items.FirstOrDefault()?.RecordSubType ?? "-",
            text);

        // 解析成功后增加今日使用次数（best-effort，统计失败不阻塞解析）
        try { await _membership.IncrementAiNoteUsageAsync(uid, ct); } catch { /* 次数统计失败不阻塞 */ }

        return new AiNoteParseBatchResponse { Items = items };
    }

    private async Task<List<AiNoteParseItem>> ParseByAiAsync(string text, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var prompt = SystemPrompt.Replace("{NowText}", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        var (raw, _) = await _ai.ChatAsync(prompt, text, ct);
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
    /// 基于规则的多条解析：委托给共享层 AiNoteRuleParser。
    /// 保留公开方法以便单元测试和外部调用。
    /// </summary>
    public List<AiNoteParseItem> ParseByRulesMulti(string text, DateTime? now = null)
        => AiNoteRuleParser.ParseMulti(text, now);

    /// <summary>基于正则的降级解析：委托给共享层 AiNoteRuleParser。公开以便单元测试访问。</summary>
    public AiNoteParseItem ParseByRules(string text, DateTime? now = null)
        => AiNoteRuleParser.Parse(text, now);
}
