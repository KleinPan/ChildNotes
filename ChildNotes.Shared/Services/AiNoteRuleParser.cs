using System.Globalization;
using System.Text.RegularExpressions;
using ChildNotes.Shared.Constants;
using ChildNotes.Shared.Dtos;

namespace ChildNotes.Shared.Services;

/// <summary>
/// AI 智能记规则降级解析器：前后端共用。
/// 将自然语言文本解析为结构化育儿记录（基于正则和关键词匹配）。
/// 当 AI 不可用时，用此降级解析保证基本可用性。
/// </summary>
public static class AiNoteRuleParser
{
    // ===== 预编译正则（SourceGenerator 在 Shared 层不可用，用 static readonly）=====

    private static readonly Regex FeedAmountRegex =
        new(@"(\d+)(?:\s*多)?\s*(ml|毫升|mL|奶粉|母乳|奶)", RegexOptions.Compiled);

    private static readonly Regex BreastRegex =
        new(@"(?:左|left)\s*(\d+)\s*(?:分|min|分钟)?.*?(?:右|right)\s*(\d+)\s*(?:分|min|分钟)?", RegexOptions.Compiled);

    private static readonly Regex SleepDurationRegex =
        new(@"(\d+)\s*(?:分|min|分钟)", RegexOptions.Compiled);

    private static readonly Regex TempRegex =
        new(@"(\d+(?:\.\d+)?)\s*(?:℃|度)", RegexOptions.Compiled);

    private static readonly Regex GrowthRegex =
        new(@"(?:身高|高)\s*(\d+(?:\.\d+)?)\s*(?:cm|厘米)?.*(?:体重|重)\s*(\d+(?:\.\d+)?)\s*(?:kg|公斤|斤)?", RegexOptions.Compiled);

    private static readonly Regex TimeRegex =
        new(@"(\d{1,2})\s*(?:点|:|：)\s*(半|\d{1,2})?", RegexOptions.Compiled);

    private static readonly Regex SleepRangeRegex =
        new(@"(\d{1,2})\s*(?:点|:|：)\s*(半|\d{0,2})\s*睡到\s*(\d{1,2})\s*(?:点|:|：)\s*(半|\d{0,2})", RegexOptions.Compiled);

    private static readonly Regex MlAmountRegex =
        new(@"(\d+)\s*(?:ml|毫升|mL)", RegexOptions.Compiled);

    // ===== 辅食关键词/单位 =====

    private static readonly string[] CompFoodKeywords = { "泥", "粥", "米粉", "面条", "辅食", "蛋黄", "肉泥", "果泥" };

    // ===== 异常关键词 =====

    private static readonly string[] AbnKeywords = { "发烧", "发热", "咳嗽", "呕吐", "吐奶", "腹泻", "拉肚子", "异常", "不舒服", "感冒", "鼻塞", "流涕" };

    #region 多条解析

    /// <summary>
    /// 多条解析：先用 NoteSplitter 切分复合语句，再逐段解析。
    /// 若切分后所有段都解析失败，回退到对原句调一次 Parse，避免切分破坏整体语义。
    /// </summary>
    public static List<AiNoteParseItem> ParseMulti(string text, DateTime? now = null)
    {
        var segments = NoteSplitter.Split(text);
        if (segments.Count == 0)
            return new List<AiNoteParseItem> { Parse(text, now) };

        var results = new List<AiNoteParseItem>(segments.Count);
        foreach (var seg in segments)
        {
            var item = Parse(seg, now);
            // 兜底未识别（activity + 原文）的段不加入结果，避免噪声
            // 但如果整句只有一段且未识别，仍需返回兜底
            if (item.RecordType == RecordType.Activity && item.Confidence <= 0.2 && segments.Count > 1)
                continue;
            results.Add(item);

            // 复合句后处理：若当前段解析出 feed，但原段同时含明确的 water 表述
            // （如"喝了110奶粉和10ml水"），补充一条 water 记录，避免丢失。
            // NoteSplitter 不切分"和"字（太通用易误伤），故在此针对性补解。
            if (item.RecordType == RecordType.Feed && ContainsWaterAmount(seg))
            {
                var waterItem = ParseWater(seg);
                // 仅在 water 提取出水量时才补充（避免无意义的重复记录）
                if (waterItem.Amount.HasValue)
                    results.Add(waterItem);
            }
        }
        // 若全部段都被跳过（罕见），回退到原句解析
        if (results.Count == 0)
            results.Add(Parse(text, now));
        return results;
    }

    /// <summary>
    /// 判断文本是否含明确的"水量"表述（X ml/毫升 水）。
    /// 用于复合句后处理：feed 段同时含水量时需补充 water 记录。
    /// </summary>
    private static bool ContainsWaterAmount(string text)
    {
        if (!text.Contains("水"))
            return false;
        return MlAmountRegex.Match(text).Success;
    }

    #endregion

    #region 单条规则解析主逻辑

    /// <summary>
    /// 单条解析：覆盖最常见的育儿记录表述。
    /// 判定优先级：water → supplement → feed(瓶喂) → feed(亲喂) → diaper → sleep → temperature → growth → complementary → pump → abnormal → 兜底 activity。
    /// </summary>
    public static AiNoteParseItem Parse(string text, DateTime? now = null)
    {
        // 0) water（喝水）：必须在 feed/supplement 之前判定，避免"喝10ml水"被误判为喂奶或补给
        if (IsWaterLike(text))
        {
            return ParseWater(text, now);
        }

        // 0b) supplement（用药/营养）：必须在 feed 之前判定，避免"喝了半包药"被误判为喂奶
        if (IsSupplementLike(text))
        {
            return ParseSupplement(text, now);
        }

        // 1) 喂奶：奶量
        var feedMlMatch = FeedAmountRegex.Match(text);
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
                Time = ExtractTime(text, now),
                Summary = "瓶喂 " + amount + (hasExplicitUnit ? "ml" : ""),
                Confidence = hasExplicitUnit ? 0.6 : 0.5,
                Source = ParseSource.Rule,
            };
        }

        // 亲喂（右单位可选，兼容"左10右15分"省略中间单位的写法）
        var breastMatch = BreastRegex.Match(text);
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
                Time = ExtractTime(text, now),
                Summary = $"亲喂 左{left} 右{right}分钟",
                Confidence = 0.6,
                Source = ParseSource.Rule,
            };
        }

        // 2) 换尿布（含大便/小便相关各种口语表述）
        if (IsDiaperLike(text))
        {
            return ParseDiaper(text, now);
        }

        // 3) 睡眠
        var sleepMatch = SleepDurationRegex.Match(text);
        var hasSleepKw = text.Contains("睡") || text.Contains("入睡") || text.Contains("小睡") || text.Contains("睡到") || text.Contains("睡觉");
        // 提取"X睡到Y"格式的起止时间
        var (sleepStart, sleepEnd) = ExtractSleepRange(text);

        if (sleepMatch.Success && hasSleepKw)
        {
            var st = sleepStart ?? ExtractTime(text, now);
            return new AiNoteParseItem
            {
                RecordType = RecordType.Sleep,
                Duration = int.TryParse(sleepMatch.Groups[1].Value, out var d) ? d : null,
                Time = st,
                StartTime = st,
                EndTime = sleepEnd,
                Summary = sleepMatch.Groups[1].Value + "分钟睡眠",
                Confidence = 0.6,
                Source = ParseSource.Rule,
            };
        }
        // "X点Y睡到A点B" 格式：计算时长
        if (!sleepMatch.Success && text.Contains("睡到"))
        {
            var dur = TryCalcSleepDuration(text);
            if (dur.HasValue)
            {
                var st = sleepStart ?? ExtractTime(text, now);
                return new AiNoteParseItem
                {
                    RecordType = RecordType.Sleep,
                    Duration = dur,
                    Time = st,
                    StartTime = st,
                    EndTime = sleepEnd,
                    Summary = dur + "分钟睡眠",
                    Confidence = 0.6,
                    Source = ParseSource.Rule,
                };
            }
        }
        if (hasSleepKw)
        {
            var st = sleepStart ?? ExtractTime(text, now);
            return new AiNoteParseItem
            {
                RecordType = RecordType.Sleep,
                Time = st,
                StartTime = st,
                EndTime = sleepEnd,
                Summary = "睡眠",
                Confidence = 0.55,
                Source = ParseSource.Rule,
            };
        }

        // 4) 体温
        var tempMatch = TempRegex.Match(text);
        if (tempMatch.Success)
        {
            var t = decimal.TryParse(tempMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null;
            return new AiNoteParseItem
            {
                RecordType = RecordType.Temperature,
                Temperature = t,
                Time = ExtractTime(text, now),
                Summary = t.HasValue ? $"体温 {t}℃" : "体温",
                Confidence = 0.6,
                Source = ParseSource.Rule,
            };
        }

        // 5) 身高体重
        var growthMatch = GrowthRegex.Match(text);
        if (growthMatch.Success)
        {
            decimal? h = decimal.TryParse(growthMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var hv) ? hv : null;
            decimal? w = growthMatch.Groups[2].Success && decimal.TryParse(growthMatch.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var wv) ? wv : null;
            return new AiNoteParseItem
            {
                RecordType = RecordType.Growth,
                Height = h,
                Weight = w,
                Time = ExtractTime(text, now),
                Summary = $"身高{h}cm 体重{w}kg",
                Confidence = 0.55,
                Source = ParseSource.Rule,
            };
        }

        // 6) 辅食：含"辅食/泥/粥/米粉/面条"或"吃了X克/勺/碗"
        if (TryParseComplementary(text, out var compItem, now)) return compItem;

        // 7) 吸奶：含"吸奶/吸了Xml"
        if (TryParsePump(text, out var pumpItem, now)) return pumpItem;

        // 8) 异常：含"发烧/咳嗽/呕吐/腹泻/异常"
        if (TryParseAbnormal(text, out var abnItem, now)) return abnItem;

        // 兜底
        return new AiNoteParseItem
        {
            RecordType = RecordType.Activity,
            Note = text,
            Time = ExtractTime(text, now),
            Summary = "未识别记录",
            Confidence = 0.2,
            Source = ParseSource.Rule,
        };
    }

    #endregion

    #region Water（喝水）分支

    /// <summary>
    /// 判断文本是否为喝水（含"水"且含"喝/饮"）。须在 feed/supplement 之前判定。
    /// 但若文本同时含"奶/奶粉/母乳"等喂奶关键词，说明是复合句（如"喝了110奶粉和10ml水"），
    /// 不应整体判定为 water（否则丢失 feed 记录），交给后续切分/解析处理。
    /// </summary>
    private static bool IsWaterLike(string text)
    {
        if (!text.Contains("水") || !(text.Contains("喝") || text.Contains("饮")))
            return false;
        // 复合句：同时含喂奶关键词，不整体判定为 water
        if (text.Contains("奶") || text.Contains("母乳"))
            return false;
        return true;
    }

    /// <summary>解析喝水记录。</summary>
    private static AiNoteParseItem ParseWater(string text, DateTime? now = null)
    {
        int? amountMl = null;
        var mlMatch = MlAmountRegex.Match(text);
        if (mlMatch.Success && int.TryParse(mlMatch.Groups[1].Value, out var ml))
            amountMl = ml;

        return new AiNoteParseItem
        {
            RecordType = RecordType.Water,
            Amount = amountMl,
            Time = ExtractTime(text, now),
            Summary = amountMl.HasValue ? $"喝水{amountMl}ml" : "喝水",
            Confidence = 0.6,
            Source = ParseSource.Rule,
        };
    }

    #endregion

    #region Diaper（尿布）分支

    /// <summary>判断是否为换尿布表述。</summary>
    private static bool IsDiaperLike(string text)
    {
        return text.Contains("尿布") || text.Contains("换尿") || text.Contains("嘘嘘") || text.Contains("便便")
            || text.Contains("拉屎") || text.Contains("拉尿")
            || text.Contains("又尿又拉") || text.Contains("又拉又尿")
            || text.Contains("大便") || text.Contains("小便") || text.Contains("拉了") || text.Contains("臭臭")
            || text.Contains("粑粑") || text.Contains("拉臭") || text.Contains("尿尿")
            || Regex.IsMatch(text, @"(^|[^布])尿了") || Regex.IsMatch(text, @"(^|[^布])便了");
    }

    /// <summary>解析换尿布记录。</summary>
    private static AiNoteParseItem ParseDiaper(string text, DateTime? now = null)
    {
        if (text.Contains("干爽") || text.Contains("干燥"))
        {
            return new AiNoteParseItem
            {
                RecordType = RecordType.Diaper,
                RecordSubType = DiaperType.Dry,
                DiaperType = DiaperType.Dry,
                Time = ExtractTime(text, now),
                Summary = "换尿布 干爽",
                Confidence = 0.6,
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
        // 提取颜色/性状描述作为备注（如"黄绿色"、"稀"、"糊状"）
        var note = ExtractDiaperNote(text);
        return new AiNoteParseItem
        {
            RecordType = RecordType.Diaper,
            RecordSubType = sub,
            DiaperType = sub,
            Note = note,
            Time = ExtractTime(text, now),
            Summary = "换尿布 " + sub + (string.IsNullOrEmpty(note) ? "" : " " + note),
            Confidence = 0.6,
            Source = ParseSource.Rule,
        };
    }

    /// <summary>
    /// 从文本中提取尿布记录的颜色/性状描述作为备注。
    /// 识别"黄绿色/金黄色/墨绿色/黑色/棕色"等颜色词，以及"稀/糊状/硬/软"等性状词。
    /// 去掉时间、动作词、尿布关键词后，剩余的描述性文字作为备注。
    /// </summary>
    private static string? ExtractDiaperNote(string text)
    {
        // 颜色词正则：X色（如黄色、绿色、黄绿色、金黄色、墨绿色、棕褐色等）
        var colorMatch = Regex.Match(text, @"(?:黄|绿|黑|棕|褐|金|墨|灰|白|红)[颜色]?(?:绿|黄|黑|棕|褐|色)?色?");
        // 性状词正则：稀/糊状/硬/软/水样/蛋花状等
        var consistencyMatch = Regex.Match(text, @"稀|糊状?|水样|蛋花样?|硬|软|成形|黏液|泡沫样?|奶瓣");
        var parts = new List<string>();
        if (colorMatch.Success) parts.Add(colorMatch.Value);
        if (consistencyMatch.Success) parts.Add(consistencyMatch.Value);
        return parts.Count > 0 ? string.Join(" ", parts) : null;
    }

    #endregion

    #region Complementary（辅食）分支

    /// <summary>尝试解析辅食记录。识别"X泥/Y粥"等关键词 + "X克/勺/碗"食量。</summary>
    private static bool TryParseComplementary(string text, out AiNoteParseItem item, DateTime? now = null)
    {
        var hasFoodKw = CompFoodKeywords.Any(text.Contains);
        var amountMatch = Regex.Match(text, @"(\d+(?:\.\d+)?)\s*(克|g|个|勺|碗)");
        var hasAmount = amountMatch.Success;
        // 含辅食关键词，或含食量单位且含"吃"字
        if (!hasFoodKw && !(hasAmount && text.Contains("吃")))
        {
            item = null!;
            return false;
        }
        string? amountText = null, amountUnit = null;
        if (hasAmount)
        {
            amountText = amountMatch.Groups[1].Value;
            amountUnit = amountMatch.Groups[2].Value == "g" ? "克" : amountMatch.Groups[2].Value;
        }
        // 提取食物名称：去掉数量/单位/动词后的剩余文本
        var foodName = Regex.Replace(text, @"\d+(?:\.\d+)?\s*(?:克|g|个|勺|碗|ml|毫升)", "")
            .Replace("吃了", "").Replace("吃", "").Replace("辅食", "").Trim();
        if (string.IsNullOrWhiteSpace(foodName)) foodName = "辅食";
        item = new AiNoteParseItem
        {
            RecordType = RecordType.Complementary,
            FoodName = foodName,
            AmountText = amountText,
            AmountUnit = amountUnit,
            Time = ExtractTime(text, now),
            Summary = $"辅食 {foodName}{(amountText is null ? "" : $" {amountText}{amountUnit}")}",
            Confidence = 0.5,
            Source = ParseSource.Rule,
        };
        return true;
    }

    #endregion

    #region Pump（吸奶）分支

    /// <summary>尝试解析吸奶记录。识别"吸奶/吸了Xml"。</summary>
    private static bool TryParsePump(string text, out AiNoteParseItem item, DateTime? now = null)
    {
        if (!text.Contains("吸奶") && !(text.Contains("吸") && text.Contains("ml")))
        {
            item = null!;
            return false;
        }
        int? amount = null;
        var mlMatch = MlAmountRegex.Match(text);
        if (mlMatch.Success && int.TryParse(mlMatch.Groups[1].Value, out var ml))
            amount = ml;
        item = new AiNoteParseItem
        {
            RecordType = RecordType.Pump,
            Amount = amount,
            Time = ExtractTime(text, now),
            Summary = amount.HasValue ? $"吸奶{amount}ml" : "吸奶",
            Confidence = 0.5,
            Source = ParseSource.Rule,
        };
        return true;
    }

    #endregion

    #region Abnormal（异常）分支

    /// <summary>尝试解析异常记录。识别发烧/咳嗽/呕吐/腹泻等症状关键词。</summary>
    private static bool TryParseAbnormal(string text, out AiNoteParseItem item, DateTime? now = null)
    {
        if (!AbnKeywords.Any(text.Contains))
        {
            item = null!;
            return false;
        }
        decimal? temp = null;
        var tm = TempRegex.Match(text);
        if (tm.Success && decimal.TryParse(tm.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var t))
            temp = t;
        item = new AiNoteParseItem
        {
            RecordType = RecordType.Abnormal,
            Temperature = temp,
            Note = text,
            Time = ExtractTime(text, now),
            Summary = temp.HasValue ? $"异常 体温{temp}℃" : "异常症状",
            Confidence = 0.5,
            Source = ParseSource.Rule,
        };
        return true;
    }

    #endregion

    #region Supplement（用药/营养）分支

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
    private static AiNoteParseItem ParseSupplement(string text, DateTime? now = null)
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
            Time = ExtractTime(text, now),
            Summary = (string.IsNullOrEmpty(doseDisplay) ? "" : doseDisplay + " ") + (name ?? (isNutrition ? "补充剂记录" : "用药记录")),
            Confidence = 0.5,
            Source = ParseSource.Rule,
        };
    }

    /// <summary>从文本中提取药品/营养品名称（去掉时间、时段词、剂量、动作词）。</summary>
    private static string? ExtractSupplementName(string text)
    {
        var s = text;
        // 去掉时间（含"8:17"、"8点17"等）
        s = TimeRegex.Replace(s, "");
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

    #endregion

    #region 时间解析辅助

    /// <summary>
    /// 从文本中提取时间。
    /// 默认返回 "HH:mm" 格式；当文本含"昨晚/昨天/前天"等相对日期词时，
    /// 返回 "yyyy-MM-dd HH:mm" 完整格式（基于今天偏移），避免凌晨补记时日期归属错误。
    /// 无法提取则返回 null。
    /// </summary>
    public static string? ExtractTime(string text, DateTime? now = null)
    {
        var m = TimeRegex.Match(text);
        if (m.Success)
        {
            var hh = int.TryParse(m.Groups[1].Value, out var h) ? h : -1;
            var mm = ParseMinuteGroup(m.Groups[2]);
            if (hh < 0 || hh > 23 || mm < 0 || mm > 59) return null;

            // 处理 12 小时制表述：
            // 1) 显式 PM 时段词（下午/晚上/傍晚/夜里/夜晚）+ 1~11 点 → +12
            // 2) 显式 AM 时段词（上午/早上/凌晨/清晨）→ 保持 AM
            // 3) 无时段词 → 取最近的过去时刻（AM/PM 两个候选中选 <= 当前且最近的）
            if (hh < 12)
            {
                bool isExplicitPm = text.Contains("晚上") || text.Contains("下午") || text.Contains("傍晚") || text.Contains("夜里") || text.Contains("夜晚");
                bool isExplicitAm = text.Contains("上午") || text.Contains("早上") || text.Contains("凌晨") || text.Contains("清晨");
                if (isExplicitPm)
                {
                    hh += 12;
                }
                else if (!isExplicitAm)
                {
                    // 无显式时段词：取最近的过去时刻
                    // 候选 AM 时刻 = today hh:mm；候选 PM 时刻 = today (hh+12):mm
                    // 选择 <= 当前时间且最近的；若都 > 当前时间（罕见，如当前 1:00 输入"2点"），取 AM
                    var n = now ?? DateTime.Now;
                    var amCandidate = new DateTime(n.Year, n.Month, n.Day, hh, mm, 0);
                    var pmCandidate = new DateTime(n.Year, n.Month, n.Day, hh + 12, mm, 0);
                    if (pmCandidate <= n && (amCandidate > n || pmCandidate > amCandidate))
                        hh += 12; // PM 候选更近且已过去
                    // 否则保持 AM
                }
                // 显式 AM 保持不变
            }
            var hhMm = $"{hh:D2}:{mm:D2}";

            // 相对日期词：返回完整日期时间，避免 NormalizeTime 用 DateTime.Today 拼成今天
            var dateOffset = GetRelativeDateOffset(text);
            if (dateOffset.HasValue)
                return (now ?? DateTime.Now).Date.AddDays(dateOffset.Value).ToString("yyyy-MM-dd ") + hhMm;
            return hhMm;
        }
        return null;
    }

    /// <summary>
    /// 识别文本中的相对日期词，返回相对于"今天"的偏移天数。
    /// 昨晚/昨天/昨夜 → -1；前天 → -2；大前天 → -3。
    /// 不含相对日期词返回 null（包括"今天/今早/今晚"，这些就是今天，偏移 0 但不特殊处理）。
    /// </summary>
    private static int? GetRelativeDateOffset(string text)
    {
        if (text.Contains("昨晚") || text.Contains("昨天") || text.Contains("昨夜"))
            return -1;
        if (text.Contains("大前天"))
            return -3;
        if (text.Contains("前天"))
            return -2;
        return null;
    }

    /// <summary>计算"X点Y睡到A点B"格式睡眠时长（分钟）。支持 12 小时制跨午/跨日。</summary>
    private static int? TryCalcSleepDuration(string text)
    {
        var m = SleepRangeRegex.Match(text);
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

    /// <summary>从"X睡到Y"格式提取睡眠起止时间，返回 ("HH:mm", "HH:mm")；无法提取则返回 (null, null)。</summary>
    private static (string? Start, string? End) ExtractSleepRange(string text)
    {
        var m = SleepRangeRegex.Match(text);
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
    internal static int ParseMinuteGroup(System.Text.RegularExpressions.Group g)
    {
        if (!g.Success) return 0;
        var v = g.Value;
        if (string.IsNullOrEmpty(v)) return 0;
        if (v == "半") return 30;
        return int.TryParse(v, out var n) ? n : 0;
    }

    #endregion

    #region 时间归一化（前后端共用）

    /// <summary>
    /// 将时间字符串归一化为指定格式。
    /// 支持 "HH:mm"、"11点半" 等中文时间、ISO 日期等输入。
    /// <paramref name="format"/> 传入 "O" 则输出 ISO 8601（前端本地存储），
    /// 传入 "yyyy-MM-dd HH:mm" 则输出后端格式。
    /// </summary>
    public static string NormalizeTime(string time, string format = "O")
    {
        // 优先尝试解析为完整日期时间（ISO 格式等）
        if (DateTime.TryParse(time, out var dt)) return dt.ToString(format);
        // 尝试解析中文时间格式（如"11点半"、"8:30"）
        var m = TimeRegex.Match(time);
        if (m.Success)
        {
            var hh = int.TryParse(m.Groups[1].Value, out var h) ? h : DateTime.Now.Hour;
            var mm = ParseMinuteGroup(m.Groups[2]);
            var baseTime = DateTime.Today.Add(new TimeSpan(hh, mm, 0));
            return baseTime.ToString(format);
        }
        // 纯 "HH:mm" 格式
        if (Regex.IsMatch(time, @"^\d{1,2}:\d{2}$") && TimeSpan.TryParse(time, out var ts))
        {
            return DateTime.Today.Add(ts).ToString(format);
        }
        return DateTime.Now.ToString(format);
    }

    /// <summary>
    /// 对 LLM 返回的时间做"取最近过去时刻"后处理。
    /// 仅当原始输入文本不含显式时段词（上午/早上/凌晨/下午/晚上/傍晚/夜里/夜晚/清晨），
    /// 且 LLM 返回的时间小时数在 1~11 范围时，应用算法：
    /// - 计算 AM 候选（hh:mm）和 PM 候选（hh+12:mm）
    /// - 选 &lt;= 当前时间且最近的；都 > 当前时间则取 AM
    /// 这样可以修正 LLM 不遵守 prompt 规则导致的时间推断错误。
    /// </summary>
    public static string NormalizeAmbiguousTime(string llmTime, string originalText, string format = "yyyy-MM-dd HH:mm", DateTime? now = null)
    {
        // 原始输入含显式时段词，不干预
        bool isExplicitPm = originalText.Contains("晚上") || originalText.Contains("下午") || originalText.Contains("傍晚") || originalText.Contains("夜里") || originalText.Contains("夜晚");
        bool isExplicitAm = originalText.Contains("上午") || originalText.Contains("早上") || originalText.Contains("凌晨") || originalText.Contains("清晨");
        if (isExplicitPm || isExplicitAm) return llmTime;

        // 从原始输入提取用户实际说的小时数，判断是否为 24 小时制表述（13~23点）
        var timeMatch = TimeRegex.Match(originalText);
        if (timeMatch.Success && int.TryParse(timeMatch.Groups[1].Value, out var userHh))
        {
            // 用户显式说了 13~23 点（24 小时制），不干预
            if (userHh >= 13 && userHh <= 23) return llmTime;
        }

        // 解析 LLM 返回的时间（支持 "HH:mm" 和 "yyyy-MM-dd HH:mm" 两种格式）
        int hh24, mm;
        if (TimeSpan.TryParse(llmTime, out var ts))
        {
            hh24 = ts.Hours;
            mm = ts.Minutes;
        }
        else if (DateTime.TryParse(llmTime, out var dt))
        {
            hh24 = dt.Hour;
            mm = dt.Minute;
        }
        else
        {
            return llmTime; // 无法解析，不干预
        }

        // 转为 12 小时制小时数（0点和12点都映射为12，但这两个值不触发歧义，下面会过滤）
        int hh12 = hh24 % 12;
        // 仅对 1~11 点范围做修正（0点和12点无 AM/PM 歧义）
        if (hh12 == 0) return llmTime;

        var n = now ?? DateTime.Now;
        var amCandidate = new DateTime(n.Year, n.Month, n.Day, hh12, mm, 0);
        var pmCandidate = new DateTime(n.Year, n.Month, n.Day, hh12 + 12, mm, 0);
        DateTime chosen;
        if (pmCandidate <= n && (amCandidate > n || pmCandidate > amCandidate))
            chosen = pmCandidate; // PM 候选更近且已过去
        else
            chosen = amCandidate; // 取 AM

        return chosen.ToString(format);
    }

    /// <summary>
    /// 将完整时间的日期部分与 "HH:mm" 时间部分拼接，支持跨日（endTime &lt; startTime 时日期 +1）。
    /// <paramref name="format"/> 传入 "O" 则输出 ISO 8601，传入 "yyyy-MM-dd HH:mm" 则输出后端格式。
    /// </summary>
    public static string CombineDateAndTime(string fullTime, string hhMm, string format = "O")
    {
        if (!DateTime.TryParse(fullTime, out var baseDt)) return fullTime;
        // 若 hhMm 是完整日期时间格式（如 LLM 返回 "2026-07-10 20:05"），直接归一化返回
        if (DateTime.TryParse(hhMm, out var fullT)) return fullT.ToString(format);
        if (!TimeSpan.TryParse(hhMm, out var t)) return fullTime;
        var result = baseDt.Date.Add(t);
        // 若结束时间小于开始时间，说明跨午夜，日期 +1
        if (t < baseDt.TimeOfDay) result = result.AddDays(1);
        return result.ToString(format);
    }

    #endregion

    #region 补给用药名称补全

    /// <summary>
    /// 系统默认补充剂标签（与前端 SupplementFormViewModel.SupplementCommonItems 一致）。
    /// 前后端共享，确保 server/local 两种解析路径返回的名称规范一致。
    /// </summary>
    public static readonly string[] DefaultSupplementNames = { "维生素D", "益生菌", "DHA", "钙剂", "铁剂" };

    /// <summary>
    /// 系统默认用药标签（与前端 SupplementFormViewModel.MedicineCommonItems 一致）。
    /// </summary>
    public static readonly string[] DefaultMedicineNames = { "泰诺林", "布洛芬", "美林", "蒙脱石散", "口服补液盐" };

    /// <summary>
    /// 常见补充剂/营养品知识库（用于 AI 解析时名称补全，比表单默认标签更全）。
    /// 仅含药品通用名/通用类别名，不含品牌名（如伊可新、星鲨、妈咪爱等）。
    /// </summary>
    public static readonly string[] CommonSupplementKnowledge = DefaultSupplementNames.Concat(new[]
    {
        "维生素AD", "维生素A", "维生素D3",
        "维生素K1", "复合维生素", "鱼肝油",
        "乳酸菌", "双歧杆菌", "枯草杆菌二联活菌",
        "藻油DHA", "ARA",
        "碳酸钙", "乳酸钙", "葡萄糖酸钙", "维生素D钙",
        "葡萄糖酸亚铁", "蛋白琥珀酸铁", "右旋糖酐铁",
        "葡萄糖酸锌", "甘草锌",
    }).ToArray();

    /// <summary>
    /// 常见药品知识库（用于 AI 解析时名称补全，比表单默认标签更全）。
    /// 仅含药品通用名/通用剂型名，不含品牌名（如思密达、百多邦、易坦静等）。
    /// </summary>
    public static readonly string[] CommonMedicineKnowledge = DefaultMedicineNames.Concat(new[]
    {
        // 退热镇痛
        "对乙酰氨基酚", "对乙酰氨基酚滴剂", "对乙酰氨基酚口服混悬液",
        "布洛芬混悬液", "布洛芬滴剂", "小儿布洛芬",
        "小儿氨酚黄那敏颗粒",
        // 止泻/胃肠
        "口服补液盐III", "口服补液盐III散",
        "枯草杆菌二联活菌颗粒", "布拉氏酵母菌", "醒脾养儿颗粒",
        // 止咳化痰
        "小儿止咳糖浆", "氨溴索", "盐酸氨溴索口服溶液",
        "小儿肺热咳喘口服液", "小儿化痰止咳颗粒",
        // 抗过敏
        "西替利嗪", "氯雷他定", "地氯雷他定",
        // 外用/皮肤
        "炉甘石洗剂", "氧化锌软膏", "红霉素软膏", "莫匹罗星软膏",
        // 中成药
        "宝泰康颗粒", "小儿七星茶颗粒", "保婴丹", "猴枣散",
        // 其他
        "生理盐水喷鼻剂", "海盐水喷雾",
    }).ToArray();

    /// <summary>
    /// 将 AI/规则解析返回的补给名称匹配到标签库，返回完整药剂名。
    /// 匹配优先级：精确匹配 → 包含匹配（取最长项）。
    /// 候选标签：同类型知识库优先（含表单默认标签 + 扩充知识库），跨类型知识库兜底；
    /// 前端可传入用户自定义标签作为额外候选（优先级最高）。
    /// 匹配不到则返回原始名称。
    /// </summary>
    /// <param name="aiName">AI/规则解析返回的名称</param>
    /// <param name="subType">子类型 medicine/supplement</param>
    /// <param name="extraCandidates">额外候选标签（前端传入用户自定义标签），同类型在前</param>
    public static string? ResolveSupplementName(string? aiName, string? subType, IEnumerable<string>? extraCandidates = null)
    {
        if (string.IsNullOrWhiteSpace(aiName)) return aiName;
        var raw = aiName.Trim();

        var isMedicine = subType == "medicine";
        var knowledgeSameType = isMedicine ? CommonMedicineKnowledge : CommonSupplementKnowledge;
        var knowledgeCrossType = isMedicine ? CommonSupplementKnowledge : CommonMedicineKnowledge;

        // 候选列表：额外候选（前端自定义，优先） → 同类型知识库 → 跨类型知识库
        var candidates = (extraCandidates ?? Array.Empty<string>())
            .Concat(knowledgeSameType)
            .Concat(knowledgeCrossType)
            .Distinct()
            .ToList();

        // 1. 精确匹配（忽略大小写）
        var exact = candidates.FirstOrDefault(c =>
            string.Equals(c, raw, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        // 2. 包含匹配：AI 名称是某标签的子串（如"维D"匹配"维生素D"），或反过来
        // 优先选最长的匹配项（更具体）
        var contains = candidates
            .Where(c => c.Contains(raw, StringComparison.OrdinalIgnoreCase)
                     || raw.Contains(c, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.Length)
            .FirstOrDefault();
        if (contains is not null) return contains;

        return raw;
    }

    #endregion
}
