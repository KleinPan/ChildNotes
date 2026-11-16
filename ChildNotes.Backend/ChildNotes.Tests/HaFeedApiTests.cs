using ChildNotes.Core.Dtos;
using ChildNotes.Shared.Constants;
using ChildNotes.Shared.Dtos;
using ChildNotes.Shared.Services;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace ChildNotes.Tests;

/// <summary>
/// HA（Home Assistant）集成接口测试：GET /api/ha/feeding 的 X-Api-Key 鉴权与数据返回。
/// 每个测试方法独立 factory + 独立内存数据库，保证隔离。
/// </summary>
public class HaFeedApiTests
{
    private const string ApiKey = "ha-test-key-0123456789abcdef";

    private static async Task<HttpClient> NewAuthClientAsync(ApiFactory factory, string email)
    {
        var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/send-code", new SendCodeRequest { Email = email });
        resp.EnsureSuccessStatusCode();
        var code = factory.GetLastCode(email) ?? throw new InvalidOperationException($"未捕获到 {email} 的验证码");
        var verifyResp = await client.PostAsJsonAsync("/api/auth/verify-code",
            new VerifyCodeRequest { Email = email, Code = code });
        var body = await verifyResp.Content.ReadAsStringAsync();
        Assert.True(verifyResp.IsSuccessStatusCode, body);
        var json = JsonDocument.Parse(body).RootElement;
        client.DefaultRequestHeaders.Authorization = new("Bearer",
            json.GetProperty("data").GetProperty("accessToken").GetString()!);
        return client;
    }

    /// <summary>创建 factory + 配置好 API Key 的用户 + 已登录客户端。</summary>
    private static async Task<(ApiFactory factory, HttpClient client)> NewHaEnvAsync()
    {
        var email = "ha_user_" + Guid.NewGuid().ToString("N")[..6] + "@test.local";
        var factory = new ApiFactory { ConfigureHaOptions = opt => opt.ApiKeys[ApiKey] = email };
        var client = await NewAuthClientAsync(factory, email);
        return (factory, client);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage resp)
    {
        var body = await resp.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body).RootElement;
    }

    [Fact]
    public async Task Feeding_WithoutApiKey_Returns401()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        var resp = await client.GetAsync("/api/ha/feeding");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        var body = await ReadJsonAsync(resp);
        Assert.Equal("000520", body.GetProperty("state").GetString());
        Assert.Equal("INVALID_API_KEY", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Feeding_WrongApiKey_Returns401()
    {
        var env = await NewHaEnvAsync();
        using var factory = env.factory;
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "wrong-key");
        var resp = await client.GetAsync("/api/ha/feeding");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Feeding_EmptyDay_ReturnsZeroSummary()
    {
        var env = await NewHaEnvAsync();
        using var factory = env.factory;
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        var resp = await client.GetAsync("/api/ha/feeding");
        Assert.True(resp.IsSuccessStatusCode, await resp.Content.ReadAsStringAsync());
        var body = await ReadJsonAsync(resp);
        Assert.Equal("000000", body.GetProperty("state").GetString());
        var data = body.GetProperty("data");
        Assert.Equal(0, data.GetProperty("summary").GetProperty("count").GetInt32());
        Assert.Empty(data.GetProperty("records").EnumerateArray());
        // 时间字段可按 ISO 8601 带时区偏移解析
        Assert.True(DateTimeOffset.TryParse(data.GetProperty("serverTime").GetString(), out _));
    }

    [Fact]
    public async Task Feeding_WithRecords_ReturnsDetailAndSummary()
    {
        var env = await NewHaEnvAsync();
        using var factory = env.factory;
        var client = env.client;

        // 写入两条当日喂养记录：一条亲喂（时长），一条瓶喂（奶量+备注）
        var nowText = ChinaTime.Now.ToString("yyyy-MM-dd HH:mm");
        var breast = new FeedRecordDto
        {
            Time = nowText, Type = FeedType.Breast, Side = "left",
            LeftDuration = 10, RightDuration = 5, Note = "HA测试亲喂",
        };
        var bottle = new FeedRecordDto
        {
            Time = nowText, Type = FeedType.Bottle, Amount = 120, Note = "HA测试瓶喂",
        };
        foreach (var dto in new[] { breast, bottle })
        {
            // App 端用 JsonSerializer.Serialize 默认序列化（PascalCase），测试保持同一协议
            var content = new StringContent(JsonSerializer.Serialize(dto), Encoding.UTF8, "application/json");
            var resp = await client.PostAsync($"/api/records/{RecordType.Feed}", content);
            Assert.True(resp.IsSuccessStatusCode, await resp.Content.ReadAsStringAsync());
        }

        var haClient = factory.CreateClient();
        haClient.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        var haResp = await haClient.GetAsync("/api/ha/feeding");
        Assert.True(haResp.IsSuccessStatusCode, await haResp.Content.ReadAsStringAsync());
        var body = await ReadJsonAsync(haResp);
        Assert.Equal("000000", body.GetProperty("state").GetString());
        var data = body.GetProperty("data");

        var records = data.GetProperty("records");
        Assert.Equal(2, records.GetArrayLength());

        var first = records[0];
        Assert.Equal(FeedType.Breast, first.GetProperty("type").GetString());
        Assert.Equal("left", first.GetProperty("side").GetString());
        Assert.Equal(15, first.GetProperty("durationMin").GetInt32());
        Assert.True(first.GetProperty("amountMl").ValueKind is JsonValueKind.Null);
        Assert.Equal("HA测试亲喂", first.GetProperty("note").GetString());
        Assert.True(DateTimeOffset.TryParse(first.GetProperty("time").GetString(), out _));

        var second = records[1];
        Assert.Equal(FeedType.Bottle, second.GetProperty("type").GetString());
        Assert.Equal(120, second.GetProperty("amountMl").GetInt32());
        Assert.Equal("HA测试瓶喂", second.GetProperty("note").GetString());

        var summary = data.GetProperty("summary");
        Assert.Equal(2, summary.GetProperty("count").GetInt32());
        Assert.Equal(120, summary.GetProperty("totalMl").GetInt32());
        Assert.Equal(1, summary.GetProperty("breastCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("bottleCount").GetInt32());
        Assert.Equal(15, summary.GetProperty("totalDurationMin").GetInt32());
        Assert.Equal(FeedType.Bottle, summary.GetProperty("lastFeedType").GetString());
        Assert.True(DateTimeOffset.TryParse(summary.GetProperty("lastFeedTime").GetString(), out _));
    }

    [Fact]
    public async Task Feeding_SpecifiedDate_ReturnsThatDate()
    {
        var env = await NewHaEnvAsync();
        using var factory = env.factory;
        var haClient = factory.CreateClient();
        haClient.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        // 显式传历史日期，返回的 Date 应与请求一致（而非默认今天）
        var resp = await haClient.GetAsync("/api/ha/feeding?date=2026-01-15");
        Assert.True(resp.IsSuccessStatusCode, await resp.Content.ReadAsStringAsync());
        var body = await ReadJsonAsync(resp);
        Assert.Equal("2026-01-15", body.GetProperty("data").GetProperty("date").GetString());
    }
}
