using System.Net.Http.Json;
using System.Text.Json;
using ChildNotes.Core.Dtos;
using ChildNotes.Shared.Constants;
using ChildNotes.Shared.Dtos;
using ChildNotes.Shared.Services;
using ChildNotes.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChildNotes.Tests;

/// <summary>
/// AI 智能记解析测试：
/// - 规则降级解析：覆盖 50+ 条不同类型的事件记录（喂奶/亲喂/尿布/睡眠/体温/生长/用药）
/// - 多条切分：复合语句正确切分为多条记录
/// - 边界情况：特殊时间格式、模糊表述、空文本、过长文本
/// - 通过 DI 注入一个抛异常的 DeepSeekClient，强制走规则降级路径，保证测试稳定。
/// </summary>
public class AiNoteParseTests
{
    private static ApiFactory NewFactory() => new();

    private static async Task<HttpClient> NewAuthClientAsync(ApiFactory factory, string username)
    {
        var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Username = username,
            Password = "pass123",
            NickName = username + "-nick",
        });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("data").GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        // 创建宝宝，让记录有归属
        await client.PostAsJsonAsync("/api/baby/add", new CreateBabyRequest
        {
            Name = "测试宝",
            Gender = "boy",
            BirthDate = new DateTime(2025, 1, 1),
        });
        return client;
    }

    // ===== 规则解析单元测试（不依赖 Web，直接 new AiNoteService）=====

    private static AiNoteService NewServiceWithFailingAi()
    {
        // 用一个会抛异常的 DeepSeekClient stub，强制走规则解析路径
        var failingAi = new FailingDeepSeekClient();
        return new AiNoteService(failingAi, NullLogger<AiNoteService>.Instance);
    }

    [Theory]
    [InlineData("喝了120ml奶", RecordType.Feed, FeedType.Bottle, 120)]
    [InlineData("刚才喝了90毫升", RecordType.Feed, FeedType.Bottle, 90)]
    [InlineData("吃了80ml母乳", RecordType.Feed, FeedType.Bottle, 80)]
    [InlineData("瓶喂150ml", RecordType.Feed, FeedType.Bottle, 150)]
    // 无单位写法（数字+奶粉/奶/母乳）——规则兜底的常见盲区
    [InlineData("16点20吃了40奶粉", RecordType.Feed, FeedType.Bottle, 40)]
    [InlineData("吃了40奶", RecordType.Feed, FeedType.Bottle, 40)]
    [InlineData("喝了50母乳", RecordType.Feed, FeedType.Bottle, 50)]
    [InlineData("60奶粉", RecordType.Feed, FeedType.Bottle, 60)]
    public void RuleParse_FeedBottle_DetectsAmount(string text, string expectedType, string expectedSub, int expectedAmount)
    {
        var svc = NewServiceWithFailingAi();
        var items = svc.ParseByRulesMulti(text);
        var parsed = items[0];
        Assert.Equal(expectedType, parsed.RecordType);
        Assert.Equal(expectedSub, parsed.RecordSubType);
        Assert.Equal(expectedAmount, parsed.Amount);
    }

    [Theory]
    [InlineData("亲喂 左10分 右15分", 10, 15)]
    [InlineData("亲喂 左10分钟 右15分钟", 10, 15)]
    public void RuleParse_BreastFeed_DetectsDurations(string text, int left, int right)
    {
        var svc = NewServiceWithFailingAi();
        var items = svc.ParseByRulesMulti(text);
        var parsed = items[0];
        Assert.Equal(RecordType.Feed, parsed.RecordType);
        Assert.Equal(FeedType.Breast, parsed.RecordSubType);
        Assert.Equal(left, parsed.LeftDuration);
        Assert.Equal(right, parsed.RightDuration);
    }

    [Theory]
    [InlineData("换尿布 嘘嘘", DiaperType.Wet)]
    [InlineData("换尿布 便便", DiaperType.Dirty)]
    [InlineData("换尿布 又尿又拉", DiaperType.Both)]
    [InlineData("换尿布 干爽", DiaperType.Dry)]
    [InlineData("拉屎了", DiaperType.Dirty)]
    [InlineData("尿了", DiaperType.Wet)]
    // 大便扩展表述（原 bug：曾误识别为 supplement）
    [InlineData("拉了大便", DiaperType.Dirty)]
    [InlineData("大便了", DiaperType.Dirty)]
    [InlineData("拉了", DiaperType.Dirty)]
    [InlineData("臭臭", DiaperType.Dirty)]
    [InlineData("拉臭臭了", DiaperType.Dirty)]
    [InlineData("粑粑", DiaperType.Dirty)]
    [InlineData("拉臭", DiaperType.Dirty)]
    [InlineData("尿尿了", DiaperType.Wet)]
    [InlineData("小便了", DiaperType.Wet)]
    public void RuleParse_Diaper_DetectsSubType(string text, string expectedSub)
    {
        var svc = NewServiceWithFailingAi();
        var items = svc.ParseByRulesMulti(text);
        var parsed = items[0];
        Assert.Equal(RecordType.Diaper, parsed.RecordType);
        Assert.Equal(expectedSub, parsed.RecordSubType);
    }

    [Theory]
    [InlineData("睡了30分钟", 30)]
    [InlineData("小睡45分", 45)]
    public void RuleParse_Sleep_DetectsDuration(string text, int duration)
    {
        var svc = NewServiceWithFailingAi();
        var items = svc.ParseByRulesMulti(text);
        var parsed = items[0];
        Assert.Equal(RecordType.Sleep, parsed.RecordType);
        Assert.Equal(duration, parsed.Duration);
    }

    [Theory]
    [InlineData("体温37.5度", 37.5)]
    [InlineData("测了体温38.2℃", 38.2)]
    public void RuleParse_Temperature_DetectsValue(string text, decimal temp)
    {
        var svc = NewServiceWithFailingAi();
        var items = svc.ParseByRulesMulti(text);
        var parsed = items[0];
        Assert.Equal(RecordType.Temperature, parsed.RecordType);
        Assert.Equal(temp, parsed.Temperature);
    }

    [Fact]
    public void RuleParse_Growth_DetectsHeightWeight()
    {
        var svc = NewServiceWithFailingAi();
        var items = svc.ParseByRulesMulti("身高70cm 体重8.5kg");
        var parsed = items[0];
        Assert.Equal(RecordType.Growth, parsed.RecordType);
        Assert.Equal(70m, parsed.Height);
        Assert.Equal(8.5m, parsed.Weight);
    }

    // ===== supplement（用药/营养）规则降级测试 =====

    [Theory]
    [InlineData("8:17喝了半包保泰康颗粒", "medicine")]
    [InlineData("吃了1粒维D", "nutrition")]
    [InlineData("喝了5滴伊可新", "medicine")]
    [InlineData("吃了半包小儿氨酚黄那敏颗粒", "medicine")]
    [InlineData("服了10滴维D3", "nutrition")]
    [InlineData("吃了一勺益生菌", "nutrition")]
    public void RuleParse_Supplement_DetectsMedicineOrNutrition(string text, string expectedSub)
    {
        var svc = NewServiceWithFailingAi();
        var items = svc.ParseByRulesMulti(text);
        var parsed = items[0];
        Assert.Equal(RecordType.Supplement, parsed.RecordType);
        Assert.Equal(expectedSub, parsed.RecordSubType);
        Assert.True(parsed.Confidence > 0, "supplement 置信度应为正数");
    }

    [Fact]
    public void RuleParse_Water_ExtractsAmount()
    {
        var svc = NewServiceWithFailingAi();
        var items = svc.ParseByRulesMulti("喝了10ml水");
        var parsed = items[0];
        Assert.Equal(RecordType.Water, parsed.RecordType);
        Assert.Equal(10, parsed.Amount);
        Assert.Contains("喝水", parsed.Summary ?? "");
    }

    [Fact]
    public void RuleParse_Water_SupportsChineseUnit()
    {
        var svc = NewServiceWithFailingAi();
        var items = svc.ParseByRulesMulti("喝了5毫升水");
        var parsed = items[0];
        Assert.Equal(RecordType.Water, parsed.RecordType);
        Assert.Equal(5, parsed.Amount);
    }

    [Fact]
    public void RuleParse_Supplement_PreservesName()
    {
        var svc = NewServiceWithFailingAi();
        var items = svc.ParseByRulesMulti("8:17喝了半包保泰康颗粒");
        var parsed = items[0];
        Assert.Equal(RecordType.Supplement, parsed.RecordType);
        // 药品名称应被提取到 Name 字段（结构化），剂量拆分为 Dose + DoseUnit
        Assert.Contains("保泰康", parsed.Name ?? "");
        Assert.Equal("0.5", parsed.Dose);
        Assert.Equal("包", parsed.DoseUnit);
    }

    [Fact]
    public void RuleParse_Supplement_StripsTimePeriodWord()
    {
        var svc = NewServiceWithFailingAi();
        // "早上8:17吃了半包宝泰康颗粒" - "早上"时段词应被去除，不应残留到 Name/Dose/Summary
        var items = svc.ParseByRulesMulti("早上8:17吃了半包宝泰康颗粒");
        var parsed = items[0];
        Assert.Equal(RecordType.Supplement, parsed.RecordType);
        Assert.Contains("宝泰康", parsed.Name ?? "");
        Assert.Equal("0.5", parsed.Dose);
        Assert.Equal("包", parsed.DoseUnit);
        // 不应残留"早上"
        Assert.DoesNotContain("早上", parsed.Name ?? "");
        Assert.DoesNotContain("早上", parsed.Summary ?? "");
    }

    [Fact]
    public void RuleParse_Supplement_NotMistakenAsFeed()
    {
        var svc = NewServiceWithFailingAi();
        // "喝了半包保泰康颗粒" 含"喝"字但不应被误判为喂奶
        var items = svc.ParseByRulesMulti("喝了半包保泰康颗粒");
        var parsed = items[0];
        Assert.Equal(RecordType.Supplement, parsed.RecordType);
        Assert.NotEqual(RecordType.Feed, parsed.RecordType);
    }

    // ===== 多条切分测试 =====

    [Fact]
    public void RuleParseMulti_CompositeSentence_SplitsIntoMultipleRecords()
    {
        var svc = NewServiceWithFailingAi();
        // "11点半睡到12点40，吃了130奶粉，喝10ml水" 应切分为 3 条
        var items = svc.ParseByRulesMulti("11点半睡到12点40，吃了130奶粉，喝10ml水");
        Assert.True(items.Count >= 3, $"应切分为至少 3 条，实际 {items.Count}");

        // 第一条应为 sleep
        Assert.Equal(RecordType.Sleep, items[0].RecordType);
        // "11点半睡到12点40" 时长应为 70 分钟
        Assert.Equal(70, items[0].Duration);

        // 第二条应为 feed（130ml 奶粉）
        var feeds = items.Where(i => i.RecordType == RecordType.Feed).ToList();
        Assert.True(feeds.Count >= 1, "应至少识别出 1 条喂奶记录");

        // 应有 water 类型（10ml 水）
        var waters = items.Where(i => i.RecordType == RecordType.Water).ToList();
        Assert.True(waters.Count >= 1, "应识别出 1 条喝水记录");
        Assert.Equal(10, waters[0].Amount);
    }

    /// <summary>
    /// 回归测试：模糊量词"X多"+复合句（喂奶+喝水）应正确解析为 feed+water 两条，
    /// 不丢失 water 记录，feed amount 取 X（如"110多"→110）。
    /// 原始 bug：规则降级路径下 IsWaterLike 整体判定为 water，丢失 feed 记录；
    /// 且 FeedAmountRegex 不支持"X多"模糊量词。
    /// </summary>
    [Fact]
    public void RuleParseMulti_FuzzyAmountFeedAndWater_SplitsInto2Records()
    {
        var svc = NewServiceWithFailingAi();
        // "2:20喝了110多奶粉和10ml水" 应解析出 feed(110) + water(10) 两条
        var items = svc.ParseByRulesMulti("2:20喝了110多奶粉和10ml水");
        Assert.True(items.Count >= 2, $"应解析出至少 2 条，实际 {items.Count}");

        var feeds = items.Where(i => i.RecordType == RecordType.Feed).ToList();
        Assert.True(feeds.Count >= 1, "应识别出 1 条喂奶记录");
        Assert.Equal(110, feeds[0].Amount);
        Assert.Equal(FeedType.Bottle, feeds[0].RecordSubType);
        Assert.Equal("02:20", feeds[0].Time);

        var waters = items.Where(i => i.RecordType == RecordType.Water).ToList();
        Assert.True(waters.Count >= 1, "应识别出 1 条喝水记录");
        Assert.Equal(10, waters[0].Amount);
    }

    /// <summary>
    /// 回归测试：纯模糊量词喂奶"110多奶粉"应解析为 feed，amount=110，不被"多"字阻断。
    /// </summary>
    [Theory]
    [InlineData("110多奶粉", 110)]
    [InlineData("喝了110多奶粉", 110)]
    [InlineData("吃了90多奶粉", 90)]
    public void RuleParse_FuzzyAmountFeed_DetectsAmount(string text, int expectedAmount)
    {
        var svc = NewServiceWithFailingAi();
        var items = svc.ParseByRulesMulti(text);
        var parsed = items[0];
        Assert.Equal(RecordType.Feed, parsed.RecordType);
        Assert.Equal(FeedType.Bottle, parsed.RecordSubType);
        Assert.Equal(expectedAmount, parsed.Amount);
    }

    [Fact]
    public void RuleParseMulti_DiaperThenSleep_SplitsInto2Records()
    {
        var svc = NewServiceWithFailingAi();
        var items = svc.ParseByRulesMulti("换了尿布便便，然后睡了30分钟");
        Assert.Equal(2, items.Count);
        Assert.Equal(RecordType.Diaper, items[0].RecordType);
        Assert.Equal(DiaperType.Dirty, items[0].RecordSubType);
        Assert.Equal(RecordType.Sleep, items[1].RecordType);
        Assert.Equal(30, items[1].Duration);
    }

    [Fact]
    public void RuleParseMulti_BreastThenBottle_SplitsInto2Records()
    {
        var svc = NewServiceWithFailingAi();
        var items = svc.ParseByRulesMulti("亲喂左10右15分，又喝了50ml奶粉");
        Assert.Equal(2, items.Count);
        Assert.Equal(RecordType.Feed, items[0].RecordType);
        Assert.Equal(FeedType.Breast, items[0].RecordSubType);
        Assert.Equal(10, items[0].LeftDuration);
        Assert.Equal(15, items[0].RightDuration);
        Assert.Equal(RecordType.Feed, items[1].RecordType);
        Assert.Equal(FeedType.Bottle, items[1].RecordSubType);
        Assert.Equal(50, items[1].Amount);
    }

    [Fact]
    public void RuleParseMulti_SingleEventNotSplit()
    {
        var svc = NewServiceWithFailingAi();
        // "11点半睡到12点40" 是单个睡眠事件，"到"不应触发切分
        var items = svc.ParseByRulesMulti("11点半睡到12点40");
        Assert.Single(items);
        Assert.Equal(RecordType.Sleep, items[0].RecordType);
        Assert.Equal(70, items[0].Duration);
    }

    [Fact]
    public void RuleParseMulti_SleepRange_CalculatesDuration()
    {
        var svc = NewServiceWithFailingAi();
        // "11点半睡到12点40" 应计算出 70 分钟时长
        var items = svc.ParseByRulesMulti("11点半睡到12点40");
        Assert.Single(items);
        Assert.Equal(RecordType.Sleep, items[0].RecordType);
        Assert.Equal(70, items[0].Duration);
        Assert.Equal("11:30", items[0].Time);
    }

    [Theory]
    [InlineData("14点30分喝了120ml奶", "14:30")]
    [InlineData("3:00吃了奶", "03:00")]
    [InlineData("晚上8点睡觉", "20:00")]
    [InlineData("10点换尿布", "10:00")]
    public void RuleParse_Time_IsExtracted(string text, string expectedTime)
    {
        var svc = NewServiceWithFailingAi();
        var items = svc.ParseByRulesMulti(text);
        var parsed = items[0];
        Assert.Equal(expectedTime, parsed.Time);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Integration_EmptyText_Returns400(string text)
    {
        using var factory = NewFactory();
        var client = await NewAuthClientAsync(factory, "user_ai_empty_" + Guid.NewGuid().ToString("N")[..6]);
        var resp = await client.PostAsJsonAsync("/api/smart-analysis/parse-note", new AiNoteParseRequest { Text = text });
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(resp.IsSuccessStatusCode);
        Assert.NotEqual("000000", body.GetProperty("state").GetString());
    }

    [Fact]
    public async Task Integration_OverlongText_Returns400()
    {
        using var factory = NewFactory();
        var client = await NewAuthClientAsync(factory, "user_ai_long_" + Guid.NewGuid().ToString("N")[..6]);
        var resp = await client.PostAsJsonAsync("/api/smart-analysis/parse-note",
            new AiNoteParseRequest { Text = new string('一', 600) });
        Assert.False(resp.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Integration_Unauthenticated_Returns401()
    {
        using var factory = NewFactory();
        var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/smart-analysis/parse-note",
            new AiNoteParseRequest { Text = "测试" });
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Integration_ValidText_ReturnsBatchResponse()
    {
        using var factory = NewFactory();
        var client = await NewAuthClientAsync(factory, "user_ai_ok_" + Guid.NewGuid().ToString("N")[..6]);
        var resp = await client.PostAsJsonAsync("/api/smart-analysis/parse-note",
            new AiNoteParseRequest { Text = "喝了120ml奶" });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        // 新协议返回 { state, data: { items: [...] }, msg }
        var data = body.GetProperty("data");
        Assert.True(data.TryGetProperty("items", out var items), "响应应包含 data.items 字段");
        Assert.True(items.GetArrayLength() >= 1, "应至少解析出 1 条记录");
    }

    /// <summary>
    /// 覆盖 50+ 条不同类型的事件记录，验证规则解析在 AI 不可用时仍能给出合理结果。
    /// </summary>
    [Theory]
    [InlineData("喝了30ml奶")]
    [InlineData("喝了50ml奶")]
    [InlineData("喝了60ml奶")]
    [InlineData("喝了80ml奶")]
    [InlineData("喝了90ml奶")]
    [InlineData("喝了100ml奶")]
    [InlineData("喝了120ml奶")]
    [InlineData("喝了150ml奶")]
    [InlineData("喝了180ml奶")]
    [InlineData("喝了200ml奶")]
    [InlineData("吃了210ml奶粉")]
    [InlineData("瓶喂240ml")]
    [InlineData("亲喂 左5分 右5分")]
    [InlineData("亲喂 左10分 右10分")]
    [InlineData("亲喂 左15分 右20分")]
    [InlineData("亲喂 左20分 右15分")]
    [InlineData("换尿布 嘘嘘")]
    [InlineData("换尿布 便便")]
    [InlineData("换尿布 又尿又拉")]
    [InlineData("换尿布 干爽")]
    [InlineData("尿了")]
    [InlineData("拉屎了")]
    [InlineData("拉屎加尿")]
    [InlineData("换尿布 嘘嘘多")]
    [InlineData("换尿布 软便")]
    [InlineData("睡了30分钟")]
    [InlineData("睡了45分钟")]
    [InlineData("睡了60分钟")]
    [InlineData("睡了1小时30分钟")]
    [InlineData("小睡20分")]
    [InlineData("入睡40分")]
    [InlineData("体温36.5度")]
    [InlineData("体温37.0度")]
    [InlineData("体温37.5度")]
    [InlineData("体温38.0度")]
    [InlineData("体温38.5度")]
    [InlineData("体温39.0度")]
    [InlineData("测了体温37.8℃")]
    [InlineData("身高65cm 体重7.0kg")]
    [InlineData("身高70cm 体重8.5kg")]
    [InlineData("身高75cm 体重9.5kg")]
    [InlineData("身高80cm 体重10.5kg")]
    [InlineData("14点喝了120ml奶")]
    [InlineData("15:30换尿布")]
    [InlineData("晚上8点睡觉")]
    [InlineData("凌晨2点喝了100ml奶")]
    [InlineData("中午12点测了体温37.2℃")]
    [InlineData("早上6点换了尿布 便便")]
    [InlineData("上午9点亲喂 左10分 右15分")]
    [InlineData("下午3点小睡40分钟")]
    [InlineData("晚上10点喝了180ml奶")]
    [InlineData("吃了半包保泰康颗粒")]
    [InlineData("吃了1粒维D")]
    [InlineData("喝了5滴伊可新")]
    public void RuleParse_BulkCases_ProducesValidRecordType(string text)
    {
        var svc = NewServiceWithFailingAi();
        var items = svc.ParseByRulesMulti(text);
        var parsed = items[0];
        Assert.True(RecordType.All.Contains(parsed.RecordType),
            $"未识别出有效记录类型，输入：{text}，实际：{parsed.RecordType}");
        Assert.True(parsed.Confidence > 0, $"置信度应为正数，输入：{text}");
    }

    [Fact]
    public void RuleParse_AmbiguousFallback_DoesNotThrow()
    {
        var svc = NewServiceWithFailingAi();
        // 极度模糊的输入应降级到 activity 兜底
        var items = svc.ParseByRulesMulti("今天发生了一些事情");
        var parsed = items[0];
        Assert.Equal(RecordType.Activity, parsed.RecordType);
        Assert.True(parsed.Confidence < 0.5);
    }

    [Fact]
    public void RuleParse_SpecialTimeFormats()
    {
        var svc = NewServiceWithFailingAi();
        // 12 小时制模糊表述仍应被识别为时间
        var items1 = svc.ParseByRulesMulti("14点30分喝了120ml奶");
        Assert.Equal("14:30", items1[0].Time);

        var items2 = svc.ParseByRulesMulti("3:00吃了奶");
        Assert.Equal("03:00", items2[0].Time);

        // 没有时间表述时应返回 null（后端取当前时间）
        var items3 = svc.ParseByRulesMulti("喝了120ml奶");
        Assert.Null(items3[0].Time);
    }

    /// <summary>Stub：所有调用均抛异常，模拟 AI 服务不可用。</summary>
    private sealed class FailingDeepSeekClient : ChildNotes.Infrastructure.External.DeepSeekClient
    {
        public FailingDeepSeekClient() : base(
            new HttpClient(),
            Microsoft.Extensions.Options.Options.Create(new ChildNotes.Core.Config.DeepSeekOptions { ApiKey = "stub" }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ChildNotes.Infrastructure.External.DeepSeekClient>.Instance)
        { }

        public override Task<(string text, string model)> ChatAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
            => throw new InvalidOperationException("AI service stubbed out for tests");
    }

    /// <summary>
    /// 时间解析：显式 PM 时段词 + 1~11 点 → +12 转 24 小时制
    /// </summary>
    [Theory]
    [InlineData("下午5点吃了奶", 17, 0)]
    [InlineData("晚上8点睡觉", 20, 0)]
    [InlineData("傍晚6点半吃奶", 18, 30)]
    [InlineData("夜里3点醒了", 15, 0)]
    [InlineData("夜晚9点睡", 21, 0)]
    public void ExtractTime_ExplicitPm_Adds12(string text, int expectedHour, int expectedMinute)
    {
        var time = AiNoteRuleParser.ExtractTime(text);
        Assert.NotNull(time);
        // ExtractTime 可能返回 "HH:mm" 或 "yyyy-MM-dd HH:mm"，取末尾 5 位
        var hhMm = time!.Length >= 5 ? time.Substring(time.Length - 5) : time;
        Assert.Equal($"{expectedHour:D2}:{expectedMinute:D2}", hhMm);
    }

    /// <summary>
    /// 时间解析：显式 AM 时段词 → 保持 AM（不加 12）
    /// </summary>
    [Theory]
    [InlineData("上午5点吃了奶", 5, 0)]
    [InlineData("早上8点睡觉", 8, 0)]
    [InlineData("凌晨3点醒了", 3, 0)]
    [InlineData("清晨6点半吃奶", 6, 30)]
    public void ExtractTime_ExplicitAm_KeepsAm(string text, int expectedHour, int expectedMinute)
    {
        var time = AiNoteRuleParser.ExtractTime(text);
        Assert.NotNull(time);
        var hhMm = time!.Length >= 5 ? time.Substring(time.Length - 5) : time;
        Assert.Equal($"{expectedHour:D2}:{expectedMinute:D2}", hhMm);
    }

    /// <summary>
    /// 时间解析：无时段词时取最近的过去时刻
    /// 算法：计算 AM 候选（baseHour:00）和 PM 候选（baseHour+12:00），
    /// 选择 <= 当前时间且最接近的；若都 > 当前时间，取 AM。
    /// </summary>
    [Theory]
    [InlineData("5点吃了奶", 5, 0)]  // 小时数 5 < 12，触发 12 小时制推断
    [InlineData("8点半睡觉", 8, 30)]
    [InlineData("10点吃奶", 10, 0)]
    public void ExtractTime_NoPeriodWord_PicksNearestPast(string text, int baseHour, int baseMinute)
    {
        var time = AiNoteRuleParser.ExtractTime(text);
        Assert.NotNull(time);
        var hhMm = time!.Length >= 5 ? time.Substring(time.Length - 5) : time;
        var parts = hhMm.Split(':');
        var hh = int.Parse(parts[0]);
        var mm = int.Parse(parts[1]);

        // 推断规则：取 AM/PM 候选中 <= now 且最近的；都 > now 则取 AM
        var now = DateTime.Now;
        var amCandidate = new DateTime(now.Year, now.Month, now.Day, baseHour, baseMinute, 0);
        var pmCandidate = new DateTime(now.Year, now.Month, now.Day, baseHour + 12, baseMinute, 0);
        int expectedHour;
        if (pmCandidate <= now && (amCandidate > now || pmCandidate > amCandidate))
            expectedHour = baseHour + 12; // PM 候选更近且已过去
        else
            expectedHour = baseHour; // 取 AM
        Assert.Equal(expectedHour, hh);
        Assert.Equal(baseMinute, mm);
    }

    /// <summary>
    /// 时间解析：12~23 点的表述不触发 12 小时制推断（已是 24 小时制）
    /// </summary>
    [Theory]
    [InlineData("13点吃奶", 13, 0)]
    [InlineData("14点半睡觉", 14, 30)]
    [InlineData("23点醒了", 23, 0)]
    public void ExtractTime_24HourFormat_NoInference(string text, int expectedHour, int expectedMinute)
    {
        var time = AiNoteRuleParser.ExtractTime(text);
        Assert.NotNull(time);
        var hhMm = time!.Length >= 5 ? time.Substring(time.Length - 5) : time;
        Assert.Equal($"{expectedHour:D2}:{expectedMinute:D2}", hhMm);
    }
}
