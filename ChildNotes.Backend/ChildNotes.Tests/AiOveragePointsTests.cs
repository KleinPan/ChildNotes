using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ChildNotes.Core.Config;
using ChildNotes.Core.Dtos;
using ChildNotes.Core.Entities;
using ChildNotes.Infrastructure.Data;
using ChildNotes.Shared.Constants;
using ChildNotes.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ChildNotes.Tests;

/// <summary>
/// 免费次数用尽后积分抵扣（usePointsForOverage）测试：
/// - AI 记链路（10 次/天免费，超限抵扣 5 积分/次，本身不耗积分）
/// - AI 分析链路（1 次/周免费，每次正常耗 10 积分，超限额外抵扣 20 积分/次）
/// - 会员状态展示：抵扣单价字段 + 抵扣放行后剩余次数下限 0
/// - 失败回滚：AI 调用失败 / 正常消耗积分不足时，抵扣积分与次数均回滚
///
/// 测试环境 DeepSeek ApiKey 为空 → ChatAsync 抛 InvalidOperationException，
/// 恰好覆盖"AI 失败回滚"场景；AI 记链路用高置信度规则文本走快速路径，不依赖 AI。
/// </summary>
public class AiOveragePointsTests
{
    private static ApiFactory NewFactory() => new();

    private static async Task<HttpClient> NewAuthClientAsync(ApiFactory factory, string username)
    {
        var email = $"{username}@test.local";
        var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/send-code", new SendCodeRequest { Email = email });
        resp.EnsureSuccessStatusCode();
        var code = factory.GetLastCode(email) ?? throw new InvalidOperationException($"未捕获到 {email} 的验证码");
        var verifyResp = await client.PostAsJsonAsync("/api/auth/verify-code",
            new VerifyCodeRequest { Email = email, Code = code });
        verifyResp.EnsureSuccessStatusCode();
        var body = await verifyResp.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("data").GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        // 创建宝宝，让 AI 分析有默认归属
        await client.PostAsJsonAsync("/api/baby/add", new CreateBabyRequest
        {
            Name = "抵扣宝",
            Gender = "boy",
            BirthDate = new DateTime(2025, 6, 1),
        });
        return client;
    }

    /// <summary>查询用户当前积分余额。</summary>
    private static async Task<long> GetPointsAsync(HttpClient client)
    {
        var body = await client.GetFromJsonAsync<JsonElement>("/api/points/dashboard");
        return body.GetProperty("data").GetProperty("points").GetInt64();
    }

    /// <summary>查询会员状态（含 AI 次数与抵扣单价）。</summary>
    private static async Task<JsonElement> GetStatusAsync(HttpClient client)
        => (await client.GetFromJsonAsync<JsonElement>("/api/membership/status")).GetProperty("data");

    /// <summary>直接写库设置积分余额（绕过业务规则，用于构造积分不足场景）。</summary>
    private static async Task SetPointsAsync(ApiFactory factory, string email, int points)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChildNotesDbContext>();
        var userId = await db.AppUsers.Where(u => u.Email == email).Select(u => u.Id).FirstAsync();
        var p = await db.UserPoints.FirstAsync(x => x.UserId == userId);
        p.Points = points;
        await db.SaveChangesAsync();
    }

    /// <summary>直接写库预置本周 AI 分析已用次数（免费用户限额 1，写 1 即超限）。</summary>
    private static async Task SetAnalysisUsageAsync(ApiFactory factory, string email, int used)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChildNotesDbContext>();
        var userId = await db.AppUsers.Where(u => u.Email == email).Select(u => u.Id).FirstAsync();
        // 与 MembershipService.GetWeekStartUtc 一致：本周一 UTC 0 点
        var today = DateTime.UtcNow.Date;
        int diff = today.DayOfWeek == DayOfWeek.Sunday ? 6 : (int)today.DayOfWeek - 1;
        var weekStart = today.AddDays(-diff);
        var record = await db.AiUsageRecords.FirstOrDefaultAsync(
            x => x.UserId == userId && x.UsageType == MembershipConstants.UsageTypeAiAnalysis && x.PeriodStart == weekStart);
        if (record is null)
        {
            record = new AiUsageRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = userId,
                UsageType = MembershipConstants.UsageTypeAiAnalysis,
                PeriodStart = weekStart,
                UsedCount = used,
            };
            db.AiUsageRecords.Add(record);
        }
        else
        {
            record.UsedCount = used;
        }
        await db.SaveChangesAsync();
    }

    /// <summary>连续调用 N 次 parse-note，消耗免费次数。用高置信度规则文本，不触发 AI 调用。</summary>
    private static async Task ExhaustNoteQuotaAsync(HttpClient client, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var resp = await client.PostAsJsonAsync("/api/smart-analysis/parse-note",
                new AiNoteParseRequest { Text = "喝了120ml奶" });
            Assert.True(resp.IsSuccessStatusCode, await resp.Content.ReadAsStringAsync());
        }
    }

    // ===== AI 记链路（超限抵扣 5 积分/次，不耗正常积分）=====

    [Fact]
    public async Task AiNote_Overage_WithoutFlag_ReturnsLimitExceeded()
    {
        using var factory = NewFactory();
        var email = "note1_" + Guid.NewGuid().ToString("N")[..6];
        var client = await NewAuthClientAsync(factory, email);

        // 用完免费 10 次
        await ExhaustNoteQuotaAsync(client, MembershipConstants.FreeDailyAiNoteLimit);

        // 第 11 次不带 usePointsForOverage → AI_NOTE_LIMIT_EXCEEDED
        var resp = await client.PostAsJsonAsync("/api/smart-analysis/parse-note",
            new AiNoteParseRequest { Text = "喝了120ml奶" });
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("000520", body.GetProperty("state").GetString());
        Assert.Equal("AI_NOTE_LIMIT_EXCEEDED", body.GetProperty("code").GetString());
        // 未抵扣放行：积分不动、次数保持 10
        Assert.Equal(PointsConstants.NewUserBonusPoints, await GetPointsAsync(client));
        var status = await GetStatusAsync(client);
        Assert.Equal(MembershipConstants.FreeDailyAiNoteLimit, status.GetProperty("aiNoteUsedToday").GetInt32());
    }

    [Fact]
    public async Task AiNote_Overage_WithFlag_DeductsPointsAndSucceeds()
    {
        using var factory = NewFactory();
        var email = "note2_" + Guid.NewGuid().ToString("N")[..6];
        var client = await NewAuthClientAsync(factory, email);

        await ExhaustNoteQuotaAsync(client, MembershipConstants.FreeDailyAiNoteLimit);

        // 第 11 次带 usePointsForOverage=true → 扣 5 积分放行
        var resp = await client.PostAsJsonAsync(
            "/api/smart-analysis/parse-note?usePointsForOverage=true",
            new AiNoteParseRequest { Text = "喝了120ml奶" });
        var respBody = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, respBody);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("000000", body.GetProperty("state").GetString());
        Assert.True(body.GetProperty("data").GetProperty("items").GetArrayLength() >= 1);

        // 积分扣减：注册赠送 100 - 抵扣 5 = 95
        Assert.Equal(PointsConstants.NewUserBonusPoints - MembershipConstants.AiNoteOveragePointsCost,
            await GetPointsAsync(client));

        // 次数已超限递增（11），剩余次数下限 0 不为负；抵扣单价字段正确返回
        var status = await GetStatusAsync(client);
        Assert.Equal(MembershipConstants.FreeDailyAiNoteLimit + 1, status.GetProperty("aiNoteUsedToday").GetInt32());
        Assert.Equal(0, status.GetProperty("aiNoteRemainingToday").GetInt32());
        Assert.Equal(MembershipConstants.AiNoteOveragePointsCost, status.GetProperty("aiNoteOveragePointsCost").GetInt32());
    }

    [Fact]
    public async Task AiNote_Overage_InsufficientPoints_RejectedWithoutSideEffects()
    {
        using var factory = NewFactory();
        var email = "note3_" + Guid.NewGuid().ToString("N")[..6];
        var client = await NewAuthClientAsync(factory, email);

        await ExhaustNoteQuotaAsync(client, MembershipConstants.FreeDailyAiNoteLimit);
        // 积分余额降到抵扣单价以下（5 - 1 = 4 < 5）
        await SetPointsAsync(factory, $"{email}@test.local", MembershipConstants.AiNoteOveragePointsCost - 1);

        // 带 usePointsForOverage=true，但积分不足 → INSUFFICIENT_POINTS，无任何副作用
        var resp = await client.PostAsJsonAsync(
            "/api/smart-analysis/parse-note?usePointsForOverage=true",
            new AiNoteParseRequest { Text = "喝了120ml奶" });
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("000520", body.GetProperty("state").GetString());
        Assert.Equal("INSUFFICIENT_POINTS", body.GetProperty("code").GetString());
        Assert.Equal(MembershipConstants.AiNoteOveragePointsCost - 1, await GetPointsAsync(client));
        var status = await GetStatusAsync(client);
        Assert.Equal(MembershipConstants.FreeDailyAiNoteLimit, status.GetProperty("aiNoteUsedToday").GetInt32());
    }

    // ===== AI 分析链路（免费 1 次/周，每次正常耗 10 积分，超限额外抵扣 20 积分）=====

    [Fact]
    public async Task AiAnalysis_Overage_WithoutFlag_ReturnsLimitExceeded()
    {
        using var factory = NewFactory();
        var email = "ana1_" + Guid.NewGuid().ToString("N")[..6];
        var client = await NewAuthClientAsync(factory, email);

        // 预置本周已用 1 次（免费限额 1）→ 超限
        await SetAnalysisUsageAsync(factory, $"{email}@test.local", 1);

        var resp = await client.PostAsJsonAsync("/api/smart-analysis/generate", new GenerateAiAnalysisRequest());
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("000520", body.GetProperty("state").GetString());
        Assert.Equal("AI_LIMIT_EXCEEDED", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AiAnalysis_Overage_WithFlag_AiFailure_RollsBackPointsAndUsage()
    {
        using var factory = NewFactory();
        var email = "ana2_" + Guid.NewGuid().ToString("N")[..6];
        var client = await NewAuthClientAsync(factory, email);

        await SetAnalysisUsageAsync(factory, $"{email}@test.local", 1);

        // 带抵扣放行：扣 20 抵扣 + 扣 10 正常 → AI 失败（测试环境未配 ApiKey）→
        // 回滚 20+10=30 积分 + 递减次数
        var resp = await client.PostAsJsonAsync(
            "/api/smart-analysis/generate?usePointsForOverage=true", new GenerateAiAnalysisRequest());
        Assert.Equal(HttpStatusCode.InternalServerError, resp.StatusCode);

        // 积分与次数全部回滚
        Assert.Equal(PointsConstants.NewUserBonusPoints, await GetPointsAsync(client));
        var status = await GetStatusAsync(client);
        Assert.Equal(1, status.GetProperty("aiAnalysisUsedThisWeek").GetInt32());
        Assert.Equal(0, status.GetProperty("aiAnalysisRemainingThisWeek").GetInt32());
    }

    [Fact]
    public async Task AiAnalysis_Overage_InsufficientPointsForDeduction_Rejected()
    {
        using var factory = NewFactory();
        var email = "ana3_" + Guid.NewGuid().ToString("N")[..6];
        var client = await NewAuthClientAsync(factory, email);

        await SetAnalysisUsageAsync(factory, $"{email}@test.local", 1);
        // 积分低于抵扣单价（20 - 1 = 19 < 20）
        await SetPointsAsync(factory, $"{email}@test.local", MembershipConstants.AiAnalysisOveragePointsCost - 1);

        var resp = await client.PostAsJsonAsync(
            "/api/smart-analysis/generate?usePointsForOverage=true", new GenerateAiAnalysisRequest());
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("000520", body.GetProperty("state").GetString());
        Assert.Equal("INSUFFICIENT_POINTS", body.GetProperty("code").GetString());
        Assert.Equal(MembershipConstants.AiAnalysisOveragePointsCost - 1, await GetPointsAsync(client));
        var status = await GetStatusAsync(client);
        Assert.Equal(1, status.GetProperty("aiAnalysisUsedThisWeek").GetInt32());
    }

    [Fact]
    public async Task AiAnalysis_Overage_EnoughForDeductionButNotFullCost_RollsBack()
    {
        using var factory = NewFactory();
        var email = "ana4_" + Guid.NewGuid().ToString("N")[..6];
        var client = await NewAuthClientAsync(factory, email);

        await SetAnalysisUsageAsync(factory, $"{email}@test.local", 1);
        // 积分够抵扣 20（放行）但不够"抵扣 20 + 正常消耗 10"（30）→ 正常扣减失败
        await SetPointsAsync(factory, $"{email}@test.local",
            MembershipConstants.AiAnalysisOveragePointsCost + PointsConstants.AiAnalysisDefaultCost - 5);

        var resp = await client.PostAsJsonAsync(
            "/api/smart-analysis/generate?usePointsForOverage=true", new GenerateAiAnalysisRequest());
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("000520", body.GetProperty("state").GetString());
        Assert.Equal("INSUFFICIENT_POINTS", body.GetProperty("code").GetString());

        // 抵扣积分与次数均已回滚，避免"扣了抵扣积分却没拿到分析结果"的部分消耗
        Assert.Equal(
            MembershipConstants.AiAnalysisOveragePointsCost + PointsConstants.AiAnalysisDefaultCost - 5,
            await GetPointsAsync(client));
        var status = await GetStatusAsync(client);
        Assert.Equal(1, status.GetProperty("aiAnalysisUsedThisWeek").GetInt32());
    }

    // ===== cost 接口与状态展示 =====

    [Fact]
    public async Task AiAnalysis_Cost_ReturnsNormalAndOveragePrice()
    {
        using var factory = NewFactory();
        var client = await NewAuthClientAsync(factory, "cost_" + Guid.NewGuid().ToString("N")[..6]);

        var body = await client.GetFromJsonAsync<JsonElement>("/api/smart-analysis/cost");
        var data = body.GetProperty("data");
        Assert.Equal(PointsConstants.AiAnalysisDefaultCost, data.GetProperty("costPoints").GetInt32());
        Assert.Equal(MembershipConstants.AiAnalysisOveragePointsCost, data.GetProperty("overagePointsCost").GetInt32());
    }

    [Fact]
    public async Task Membership_Status_IncludesOverageCostFields()
    {
        using var factory = NewFactory();
        var client = await NewAuthClientAsync(factory, "stat_" + Guid.NewGuid().ToString("N")[..6]);

        var status = await GetStatusAsync(client);
        Assert.Equal(MembershipConstants.AiNoteOveragePointsCost, status.GetProperty("aiNoteOveragePointsCost").GetInt32());
        Assert.Equal(MembershipConstants.AiAnalysisOveragePointsCost, status.GetProperty("aiAnalysisOveragePointsCost").GetInt32());
        // 初始剩余次数不为负
        Assert.True(status.GetProperty("aiNoteRemainingToday").GetInt32() >= 0);
        Assert.True(status.GetProperty("aiAnalysisRemainingThisWeek").GetInt32() >= 0);
    }
}
