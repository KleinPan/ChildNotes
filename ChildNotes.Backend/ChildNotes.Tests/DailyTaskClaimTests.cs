using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ChildNotes.Core.Dtos;
using ChildNotes.Infrastructure.Data;
using ChildNotes.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ChildNotes.Tests;

/// <summary>
/// 每日任务奖励领取回归（防双领 #5）：
/// - 重复领取被拦截（TASK_ALREADY_CLAIMED），积分只入账一次
/// - weekly_growth 的 payload 周期标识为本周一日期（同周内不同日重复领取被拦截）
/// - 唯一索引 ux_task_record_daily_claim 已纳入 EF 模型（生产库并发防线）
/// </summary>
public class DailyTaskClaimTests
{
    private static ApiFactory NewFactory() => new();

    private static async Task<HttpClient> NewAuthClientWithBabyAsync(ApiFactory factory, string username)
    {
        var email = $"{username}@test.local";
        var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/send-code", new Core.Dtos.SendCodeRequest { Email = email });
        resp.EnsureSuccessStatusCode();
        var code = factory.GetLastCode(email) ?? throw new InvalidOperationException($"未捕获到 {email} 的验证码");
        var verifyResp = await client.PostAsJsonAsync("/api/auth/verify-code",
            new Core.Dtos.VerifyCodeRequest { Email = email, Code = code });
        verifyResp.EnsureSuccessStatusCode();
        var body = await verifyResp.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new("Bearer",
            body.GetProperty("data").GetProperty("accessToken").GetString()!);

        // 创建宝宝 + 一条喂奶记录，满足 daily_record/daily_feed 完成条件
        var babyResp = await client.PostAsJsonAsync("/api/baby/add", new CreateBabyRequest { Name = "宝" });
        Assert.True(babyResp.IsSuccessStatusCode, await babyResp.Content.ReadAsStringAsync());
        var feedResp = await client.PostAsJsonAsync("/api/records/feed", new FeedRecordDto
        {
            Time = DateTime.Now.ToString("O"),
            Type = "bottle",
            Amount = 100,
        });
        Assert.True(feedResp.IsSuccessStatusCode, await feedResp.Content.ReadAsStringAsync());
        return client;
    }

    private static async Task<HttpResponseMessage> ClaimAsync(HttpClient client, string taskKey)
    {
        var resp = await client.PostAsJsonAsync($"/api/points/tasks/{taskKey}/claim", new { });
        return resp;
    }

    [Fact]
    public async Task ClaimDailyFeed_SecondClaimRejected_PointsOnlyOnce()
    {
        using var factory = NewFactory();
        var client = await NewAuthClientWithBabyAsync(factory, "claim_" + Guid.NewGuid().ToString("N")[..6]);

        var first = await ClaimAsync(client, "daily_feed");
        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        var points1 = firstBody.GetProperty("data").GetProperty("points").GetInt32();

        // 重复领取（绕过前端防抖直接调 API）：业务异常，积分不再入账
        var second = await ClaimAsync(client, "daily_feed");
        Assert.False(second.IsSuccessStatusCode);
        var secondBody = await second.Content.ReadAsStringAsync();
        Assert.Contains("TASK_ALREADY_CLAIMED", secondBody);

        // 钱包只入账一次：重复领取后余额不变
        var dash = await client.GetAsync("/api/points/dashboard");
        var dashBody = await dash.Content.ReadFromJsonAsync<JsonElement>();
        var points2 = dashBody.GetProperty("data").GetProperty("points").GetInt32();
        Assert.Equal(points1, points2);
    }

    [Fact]
    public async Task WeeklyGrowth_ClaimedThisWeek_PayloadUsesWeekStartDate()
    {
        using var factory = NewFactory();
        var client = await NewAuthClientWithBabyAsync(factory, "wclaim_" + Guid.NewGuid().ToString("N")[..6]);

        // 补一条成长记录满足 weekly_growth 完成条件
        var growthResp = await client.PostAsJsonAsync("/api/records/growth", new GrowthRecordDto
        {
            Time = DateTime.Now.ToString("O"),
            Height = 70.5m,
            Weight = 8.2m,
        });
        var growthBody = await growthResp.Content.ReadAsStringAsync();
        Assert.True(growthResp.IsSuccessStatusCode, growthBody);

        var first = await ClaimAsync(client, "weekly_growth");
        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());

        // DB 校验：payload 的 date 字段 = 本周一日期（唯一索引的周期维度）
        var today = DateTime.Today;
        var weekStart = today.AddDays(-(int)today.DayOfWeek + (int)DayOfWeek.Monday);
        if (today.DayOfWeek == DayOfWeek.Sunday) weekStart = weekStart.AddDays(-7);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChildNotesDbContext>();
            var payload = await db.TaskRecords
                .Where(t => t.TaskType == "daily_task" && t.TaskKey == "weekly_growth")
                .Select(t => t.PayloadJson).SingleAsync();
            Assert.Contains(weekStart.ToString("yyyy-MM-dd"), payload);
        }

        // 同周再次领取：被拦（此时走 check 快路径）
        var second = await ClaimAsync(client, "weekly_growth");
        Assert.False(second.IsSuccessStatusCode);
        Assert.Contains("TASK_ALREADY_CLAIMED", await second.Content.ReadAsStringAsync());
    }

    [Fact]
    public void NpgsqlModel_HasUniqueClaimIndex()
    {
        // 唯一索引是生产环境并发双领的最终防线（InMemory 不模拟唯一索引，
        // 其运行时模型会丢弃带 filter 的关系索引）。用 Npgsql provider 仅构建模型
        // （不连接数据库），断言索引定义存在，防止未来误删导致迁移漂移。
        var builder = new DbContextOptionsBuilder<ChildNotesDbContext>()
            .UseNpgsql("Host=localhost;Database=schema_check;Username=x;Password=x");
        using var db = new ChildNotesDbContext(builder.Options);
        var entityType = db.Model.FindEntityType(typeof(Core.Entities.TaskRecord))!;
        // HasDatabaseName 设置的是数据库层名称（IIndex.Name 为 null），用 GetDatabaseName() 匹配
        var index = entityType.GetIndexes()
            .FirstOrDefault(i => i.GetDatabaseName() == "ux_task_record_daily_claim");
        Assert.NotNull(index);
        Assert.True(index!.IsUnique);
        Assert.Equal("task_type = 'daily_task'", index.GetFilter());
        var props = index.Properties.Select(p => p.Name).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "PayloadJson", "TaskKey", "UserId" }, props);
    }
}
