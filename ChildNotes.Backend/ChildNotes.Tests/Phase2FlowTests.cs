using System.Net.Http.Json;
using System.Text.Json;
using ChildNotes.Core.Config;
using ChildNotes.Core.Dtos;
using ChildNotes.Core.Entities;
using ChildNotes.Infrastructure.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ChildNotes.Infrastructure.Data;

namespace ChildNotes.Tests;

public class Phase2FlowTests
{
    private static ApiFactory NewFactory() => new();

    private static async Task<HttpClient> NewAuthClientAsync(ApiFactory factory, string username, string password = "pass123")
    {
        var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Username = username,
            Password = password,
            NickName = username + "-nick",
        });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("data").GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Dashboard_ReturnsPointsAndReferrerCode()
    {
        using var factory = NewFactory();
        var client = await NewAuthClientAsync(factory, "dash_" + Guid.NewGuid().ToString("N")[..6]);
        var resp = await client.GetAsync("/api/points/dashboard");
        var respBody = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, respBody);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("000000", body.GetProperty("state").GetString());
        var data = body.GetProperty("data");
        Assert.True(data.GetProperty("shareReferrerId").GetString()!.StartsWith("u_"));
        Assert.Equal(0, data.GetProperty("points").GetInt64());
    }

    [Fact]
    public async Task SignIn_AwardsPointsAndPreventsDuplicate()
    {
        using var factory = NewFactory();
        var client = await NewAuthClientAsync(factory, "sign_" + Guid.NewGuid().ToString("N")[..6]);
        var resp1 = await client.PostAsync("/api/points/sign-in", null);
        Assert.True(resp1.IsSuccessStatusCode, await resp1.Content.ReadAsStringAsync());
        var body1 = await resp1.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body1.GetProperty("data").GetProperty("points").GetInt64() >= 1);

        // 重复签到失败
        var resp2 = await client.PostAsync("/api/points/sign-in", null);
        var body2 = await resp2.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("000520", body2.GetProperty("state").GetString());
        Assert.Contains("今日已签到", body2.GetProperty("msg").GetString()!);
    }

    [Fact]
    public async Task SignInRule_ReturnsCycleAndRewards()
    {
        using var factory = NewFactory();
        var client = await NewAuthClientAsync(factory, "rule_" + Guid.NewGuid().ToString("N")[..6]);
        var resp = await client.GetAsync("/api/points/sign-in-rule");
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data");
        Assert.Equal(30, data.GetProperty("cycleDays").GetInt32());
        var rewards = data.GetProperty("rewards");
        Assert.Equal(5, rewards.GetArrayLength());
    }

    [Fact]
    public async Task JoinLottery_InsufficientPoints_Fails()
    {
        using var factory = NewFactory();
        var client = await NewAuthClientAsync(factory, "lot_" + Guid.NewGuid().ToString("N")[..6]);

        // 先创建一个抽奖活动（直接写 DB）
        string lotteryId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChildNotesDbContext>();
            var activity = new LotteryActivity
            {
                Id = Guid.NewGuid().ToString("N"),
                Title = "测试抽奖",
                Status = "active",
                CostPoints = 30,
                DrawTime = DateTime.UtcNow.AddDays(1),
                StartTime = DateTime.UtcNow.AddDays(-1),
                WinnerCount = 1,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            db.LotteryActivities.Add(activity);
            await db.SaveChangesAsync();
            lotteryId = activity.Id;
        }

        var resp = await client.PostAsync($"/api/points/lottery/{lotteryId}/join", null);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("000520", body.GetProperty("state").GetString());
        Assert.Contains("积分不足", body.GetProperty("msg").GetString()!);
    }

    [Fact]
    public async Task InviteBinding_AwardsReferrer100Points()
    {
        using var factory = NewFactory();
        var referrerClient = await NewAuthClientAsync(factory, "ref_" + Guid.NewGuid().ToString("N")[..6]);

        // 拿推荐码
        var dash = await referrerClient.GetFromJsonAsync<JsonElement>("/api/points/dashboard");
        var referrerCode = dash.GetProperty("data").GetProperty("shareReferrerId").GetString()!;

        // 被邀请人注册后调用绑定
        var invitedClient = await NewAuthClientAsync(factory, "inv_" + Guid.NewGuid().ToString("N")[..6]);
        // 通过 AuthController 没有直接绑定接口，这里直接调用 IInviteService.BindReferrerAsync
        using (var scope = factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<ChildNotesDbContext>();
            var inviteSvc = sp.GetRequiredService<Core.Services.IInviteService>();
            var invitedUser = await db.AppUsers.FirstAsync(u => u.Username.StartsWith("inv_"));
            await inviteSvc.BindReferrerAsync(invitedUser.Id, referrerCode, newUser: true);
        }

        // 推荐人积分应 +100
        var dash2 = await referrerClient.GetFromJsonAsync<JsonElement>("/api/points/dashboard");
        Assert.Equal(100, dash2.GetProperty("data").GetProperty("points").GetInt64());
        var invites = dash2.GetProperty("data").GetProperty("inviteRecords");
        Assert.Equal(1, invites.GetArrayLength());
    }

    [Fact]
    public async Task ReferrerCode_Roundtrip()
    {
        var util = new ReferrerCodeUtil("test-secret-12345");
        var userId = Guid.NewGuid().ToString("N");
        var code = util.Encode(userId);
        Assert.Equal(userId, util.Decode(code));
        // 错误 secret 解码失败
        var wrong = new ReferrerCodeUtil("wrong-secret");
        Assert.Null(wrong.Decode(code));
    }

    [Fact]
    public async Task Upload_ReturnsUrl()
    {
        using var factory = NewFactory();
        var client = await NewAuthClientAsync(factory, "up_" + Guid.NewGuid().ToString("N")[..6]);
        using var content = new MultipartFormDataContent();
        // 最小合法 PNG：8 字节签名 + 25 字节 IHDR chunk（1x1 像素）
        var pngBytes = new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, // PNG signature
            0x00, 0x00, 0x00, 0x0D, // IHDR length
            0x49, 0x48, 0x44, 0x52, // "IHDR"
            0x00, 0x00, 0x00, 0x01, // width=1
            0x00, 0x00, 0x00, 0x01, // height=1
            0x08, 0x06, 0x00, 0x00, 0x00, // bit depth=8, color type=6(RGBA), compression=0, filter=0
            0x1F, 0x15, 0xC4, 0x89, // CRC
            0x00, 0x00, 0x00, 0x0A, // IDAT length
            0x49, 0x44, 0x41, 0x54, // "IDAT"
            0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00, 0x05, 0x00, 0x01,
            0x0D, 0x0A, 0x2D, 0xB4, // CRC
            0x00, 0x00, 0x00, 0x00, // IEND length
            0x49, 0x45, 0x4E, 0x44, // "IEND"
            0xAE, 0x42, 0x60, 0x82, // CRC
        };
        var fileContent = new ByteArrayContent(pngBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "test.png");

        var resp = await client.PostAsync("/api/upload", content);
        var respBody = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, respBody);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var url = body.GetProperty("data").GetProperty("url").GetString()!;
        Assert.Contains("/uploads/", url);
        Assert.EndsWith(".png", url);
    }

    [Fact]
    public async Task Upload_RejectsDangerousExtension()
    {
        using var factory = NewFactory();
        var client = await NewAuthClientAsync(factory, "up_" + Guid.NewGuid().ToString("N")[..6]);
        using var content = new MultipartFormDataContent();
        var fileBytes = System.Text.Encoding.UTF8.GetBytes("<script>alert(1)</script>");
        content.Add(new ByteArrayContent(fileBytes), "file", "evil.html");

        var resp = await client.PostAsync("/api/upload", content);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Upload_RejectsMagicBytesMismatch()
    {
        using var factory = NewFactory();
        var client = await NewAuthClientAsync(factory, "up_" + Guid.NewGuid().ToString("N")[..6]);
        using var content = new MultipartFormDataContent();
        // 伪装成 png 但内容是 HTML
        var fileBytes = System.Text.Encoding.UTF8.GetBytes("<html>not a png</html>");
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "fake.png");

        var resp = await client.PostAsync("/api/upload", content);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task AiAnalysis_List_EmptyForNewUser()
    {
        using var factory = NewFactory();
        var client = await NewAuthClientAsync(factory, "ai_" + Guid.NewGuid().ToString("N")[..6]);
        // 先创建宝宝
        await client.PostAsJsonAsync("/api/baby/add", new CreateBabyRequest { Name = "宝" });

        var resp = await client.GetAsync("/api/smart-analysis/list");
        var respBody = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, respBody);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("000000", body.GetProperty("state").GetString());
        Assert.Equal(0, body.GetProperty("data").GetArrayLength());
    }
}
