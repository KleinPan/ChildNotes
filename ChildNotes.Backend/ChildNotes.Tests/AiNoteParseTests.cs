using System.Net.Http.Json;
using System.Text.Json;
using ChildNotes.Core.Constants;
using ChildNotes.Core.Dtos;
using ChildNotes.Shared.Constants;
using ChildNotes.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChildNotes.Tests;

/// <summary>
/// AI 智能记解析测试：
/// - 规则降级解析：覆盖 50+ 条不同类型的事件记录（喂奶/亲喂/尿布/睡眠/体温/生长）
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
        return new AiNoteService(failingAi);
    }

    [Theory]
    [InlineData("喝了120ml奶", RecordType.Feed, FeedType.Bottle, 120)]
    [InlineData("刚才喝了90毫升", RecordType.Feed, FeedType.Bottle, 90)]
    [InlineData("吃了80ml母乳", RecordType.Feed, FeedType.Bottle, 80)]
    [InlineData("瓶喂150ml", RecordType.Feed, FeedType.Bottle, 150)]
    public void RuleParse_FeedBottle_DetectsAmount(string text, string expectedType, string expectedSub, int expectedAmount)
    {
        var svc = NewServiceWithFailingAi();
        var parsed = svc.ParseByRules(text);
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
        var parsed = svc.ParseByRules(text);
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
    public void RuleParse_Diaper_DetectsSubType(string text, string expectedSub)
    {
        var svc = NewServiceWithFailingAi();
        var parsed = svc.ParseByRules(text);
        Assert.Equal(RecordType.Diaper, parsed.RecordType);
        Assert.Equal(expectedSub, parsed.RecordSubType);
    }

    [Theory]
    [InlineData("睡了30分钟", 30)]
    [InlineData("小睡45分", 45)]
    public void RuleParse_Sleep_DetectsDuration(string text, int duration)
    {
        var svc = NewServiceWithFailingAi();
        var parsed = svc.ParseByRules(text);
        Assert.Equal(RecordType.Sleep, parsed.RecordType);
        Assert.Equal(duration, parsed.Duration);
    }

    [Theory]
    [InlineData("体温37.5度", 37.5)]
    [InlineData("测了体温38.2℃", 38.2)]
    public void RuleParse_Temperature_DetectsValue(string text, decimal temp)
    {
        var svc = NewServiceWithFailingAi();
        var parsed = svc.ParseByRules(text);
        Assert.Equal(RecordType.Temperature, parsed.RecordType);
        Assert.Equal(temp, parsed.Temperature);
    }

    [Fact]
    public void RuleParse_Growth_DetectsHeightWeight()
    {
        var svc = NewServiceWithFailingAi();
        var parsed = svc.ParseByRules("身高70cm 体重8.5kg");
        Assert.Equal(RecordType.Growth, parsed.RecordType);
        Assert.Equal(70m, parsed.Height);
        Assert.Equal(8.5m, parsed.Weight);
    }

    [Theory]
    [InlineData("14点30分喝了120ml奶", "14:30")]
    [InlineData("3:00吃了奶", "03:00")]
    [InlineData("晚上8点睡觉", "20:00")]
    [InlineData("10点换尿布", "10:00")]
    public void RuleParse_Time_IsExtracted(string text, string expectedTime)
    {
        var svc = NewServiceWithFailingAi();
        var parsed = svc.ParseByRules(text);
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
    public void RuleParse_BulkCases_ProducesValidRecordType(string text)
    {
        var svc = NewServiceWithFailingAi();
        var parsed = svc.ParseByRules(text);
        Assert.True(RecordType.All.Contains(parsed.RecordType),
            $"未识别出有效记录类型，输入：{text}，实际：{parsed.RecordType}");
        Assert.True(parsed.Confidence > 0, $"置信度应为正数，输入：{text}");
    }

    [Fact]
    public void RuleParse_AmbiguousFallback_DoesNotThrow()
    {
        var svc = NewServiceWithFailingAi();
        // 极度模糊的输入应降级到 activity 兜底
        var parsed = svc.ParseByRules("今天发生了一些事情");
        Assert.Equal(RecordType.Activity, parsed.RecordType);
        Assert.True(parsed.Confidence < 0.5);
    }

    [Fact]
    public void RuleParse_SpecialTimeFormats()
    {
        var svc = NewServiceWithFailingAi();
        // 12 小时制模糊表述仍应被识别为时间
        var parsed1 = svc.ParseByRules("14点30分喝了120ml奶");
        Assert.Equal("14:30", parsed1.Time);

        var parsed2 = svc.ParseByRules("3:00吃了奶");
        Assert.Equal("03:00", parsed2.Time);

        // 没有时间表述时应返回 null（后端取当前时间）
        var parsed3 = svc.ParseByRules("喝了120ml奶");
        Assert.Null(parsed3.Time);
    }

    /// <summary>Stub：所有调用均抛异常，模拟 AI 服务不可用。</summary>
    private sealed class FailingDeepSeekClient : ChildNotes.Infrastructure.External.DeepSeekClient
    {
        public FailingDeepSeekClient() : base(
            new HttpClient(),
            Microsoft.Extensions.Options.Options.Create(new ChildNotes.Core.Config.DeepSeekOptions { ApiKey = "stub" }))
        { }

        public override Task<(string text, string model)> ChatAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
            => throw new InvalidOperationException("AI service stubbed out for tests");
    }
}
