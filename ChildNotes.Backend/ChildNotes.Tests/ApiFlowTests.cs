using ChildNotes.Core.Constants;
using ChildNotes.Core.Dtos;
using ChildNotes.Core.Services;
using ChildNotes.Shared.Constants;
using ChildNotes.Shared.Dtos;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ChildNotes.Infrastructure.Data;
using System.Net.Http.Json;
using System.Text.Json;

namespace ChildNotes.Tests;

/// <summary>
/// 集成测试：每个测试方法独立 factory + 独立内存数据库，保证隔离
/// </summary>
public class ApiFlowTests
{
    private static ApiFactory NewFactory() => new();

    private static async Task<HttpClient> NewAuthClientAsync(ApiFactory factory, string username)
    {
        // 邮箱验证码登录：username 作为 email 前缀，生成唯一邮箱
        var email = $"{username}@test.local";
        var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/send-code", new SendCodeRequest { Email = email });
        resp.EnsureSuccessStatusCode();
        // 从 stub EmailSender 取出验证码
        var code = factory.GetLastCode(email) ?? throw new InvalidOperationException($"未捕获到 {email} 的验证码");
        var verifyResp = await client.PostAsJsonAsync("/api/auth/verify-code",
            new VerifyCodeRequest { Email = email, Code = code });
        verifyResp.EnsureSuccessStatusCode();
        var body = await verifyResp.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("data").GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    [Fact]
    public async Task VerifyCode_NewEmail_CreatesUserAndReturnsTokens()
    {
        using var factory = NewFactory();
        var client = factory.CreateClient();
        var email = "user_reg_" + Guid.NewGuid().ToString("N")[..6] + "@test.local";
        var resp = await client.PostAsJsonAsync("/api/auth/send-code", new SendCodeRequest { Email = email });
        Assert.True(resp.IsSuccessStatusCode, await resp.Content.ReadAsStringAsync());
        var code = factory.GetLastCode(email)!;
        var verifyResp = await client.PostAsJsonAsync("/api/auth/verify-code",
            new VerifyCodeRequest { Email = email, Code = code });
        var verifyBody = await verifyResp.Content.ReadAsStringAsync();
        Assert.True(verifyResp.IsSuccessStatusCode, verifyBody);
        var body = await verifyResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("000000", body.GetProperty("state").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("data").GetProperty("accessToken").GetString()));
        Assert.False(string.IsNullOrEmpty(body.GetProperty("data").GetProperty("refreshToken").GetString()));
        Assert.True(body.GetProperty("data").GetProperty("newUser").GetBoolean());
    }

    [Fact]
    public async Task VerifyCode_WrongCode_Fails()
    {
        using var factory = NewFactory();
        var client = factory.CreateClient();
        var email = "user_login_" + Guid.NewGuid().ToString("N")[..6] + "@test.local";
        await client.PostAsJsonAsync("/api/auth/send-code", new SendCodeRequest { Email = email });
        // 用错误验证码
        var resp = await client.PostAsJsonAsync("/api/auth/verify-code",
            new VerifyCodeRequest { Email = email, Code = "000000" });
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("000520", body.GetProperty("state").GetString());
    }

    [Fact]
    public async Task Me_WithoutToken_Returns401()
    {
        using var factory = NewFactory();
        var client = factory.CreateClient();
        var resp = await client.GetAsync("/api/auth/me");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    /// <summary>登录并返回携带 Bearer 的客户端与 RefreshToken 明文（refresh 流程测试用）。</summary>
    private static async Task<(HttpClient client, string refreshToken)> NewAuthClientWithRefreshTokenAsync(
        ApiFactory factory, string username)
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
        var refreshToken = body.GetProperty("data").GetProperty("refreshToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new("Bearer",
            body.GetProperty("data").GetProperty("accessToken").GetString()!);
        return (client, refreshToken);
    }

    /// <summary>调用 /api/auth/refresh，成功时返回新 token 对。</summary>
    private static async Task<(string accessToken, string refreshToken)> RefreshTokensAsync(
        HttpClient client, string refreshToken)
    {
        var resp = await client.PostAsJsonAsync("/api/auth/refresh",
            new RefreshRequest { RefreshToken = refreshToken });
        var body = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, body);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return (
            json.GetProperty("data").GetProperty("accessToken").GetString()!,
            json.GetProperty("data").GetProperty("refreshToken").GetString()!);
    }

    [Fact]
    public async Task Refresh_Rotation_IssuesNewTokenPair()
    {
        using var factory = NewFactory();
        var (client, refreshToken) = await NewAuthClientWithRefreshTokenAsync(
            factory, "rf1_" + Guid.NewGuid().ToString("N")[..6]);

        var (newAccess, newRefresh) = await RefreshTokensAsync(client, refreshToken);
        Assert.False(string.IsNullOrEmpty(newAccess));
        Assert.False(string.IsNullOrEmpty(newRefresh));
        Assert.NotEqual(refreshToken, newRefresh);

        // 新 AccessToken 可正常访问鉴权接口
        client.DefaultRequestHeaders.Authorization = new("Bearer", newAccess);
        var meResp = await client.GetAsync("/api/auth/me");
        Assert.True(meResp.IsSuccessStatusCode, await meResp.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// 宽限期内重放已撤销的旧 token：应换取新 token 而非 401。
    /// 场景：Rotation 后客户端因网络超时/进程中断未保存新 token，
    /// 只能重放旧 token——401 会触发客户端软登出，造成"掉线"故障。
    /// </summary>
    [Fact]
    public async Task Refresh_ReplayedRevokedToken_WithinGrace_ReturnsNewToken()
    {
        using var factory = NewFactory();
        var (client, refreshToken) = await NewAuthClientWithRefreshTokenAsync(
            factory, "rf2_" + Guid.NewGuid().ToString("N")[..6]);

        // 第一次 refresh 成功：旧 token 已被服务端撤销（模拟客户端未收到响应）
        await RefreshTokensAsync(client, refreshToken);

        // 宽限期内（默认 120s）重放旧 token：应成功再签发新 token 对
        var (retryAccess, _) = await RefreshTokensAsync(client, refreshToken);
        Assert.False(string.IsNullOrEmpty(retryAccess));

        // 重试拿到的新 AccessToken 也可用
        client.DefaultRequestHeaders.Authorization = new("Bearer", retryAccess);
        var meResp = await client.GetAsync("/api/auth/me");
        Assert.True(meResp.IsSuccessStatusCode, await meResp.Content.ReadAsStringAsync());
    }

    /// <summary>宽限期外重放已撤销的旧 token：应返回 401（防盗用重放）。</summary>
    [Fact]
    public async Task Refresh_ReplayedRevokedToken_AfterGrace_Returns401()
    {
        using var factory = NewFactory();
        var (client, refreshToken) = await NewAuthClientWithRefreshTokenAsync(
            factory, "rf3_" + Guid.NewGuid().ToString("N")[..6]);

        await RefreshTokensAsync(client, refreshToken);

        // 把旧 token 的撤销时间改到宽限期（默认 120s）之外，模拟过期重放
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChildNotesDbContext>();
            foreach (var t in db.RefreshTokens.Where(t => t.RevokedAt != null).ToList())
                t.RevokedAt = DateTime.UtcNow.AddSeconds(-121);
            await db.SaveChangesAsync();
        }

        var resp = await client.PostAsJsonAsync("/api/auth/refresh",
            new RefreshRequest { RefreshToken = refreshToken });
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task CreateBaby_AutoCreatesOwnerMember()
    {
        using var factory = NewFactory();
        var client = await NewAuthClientAsync(factory, "user_baby_" + Guid.NewGuid().ToString("N")[..6]);
        var resp = await client.PostAsJsonAsync("/api/baby/add", new CreateBabyRequest
        {
            Name = "小宝",
            Gender = "boy",
            BirthDate = new DateTime(2025, 1, 1),
        });
        var respBody = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, respBody);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("000000", body.GetProperty("state").GetString());
        var baby = body.GetProperty("data");
        Assert.Equal("小宝", baby.GetProperty("name").GetString());

        // 列出家庭成员应该有 1 个 owner（自己）
        var famResp = await client.GetAsync("/api/baby/family/members");
        var famRespBody = await famResp.Content.ReadAsStringAsync();
        Assert.True(famResp.IsSuccessStatusCode, famRespBody);
        var famBody = await famResp.Content.ReadFromJsonAsync<JsonElement>();
        var families = famBody.GetProperty("data");
        Assert.Equal(1, families.GetArrayLength());
        var members = families[0].GetProperty("members");
        Assert.Equal(1, members.GetArrayLength());
        Assert.True(members[0].GetProperty("owner").GetBoolean());
        Assert.True(members[0].GetProperty("mine").GetBoolean());
    }

    [Fact]
    public async Task JoinFamily_AddsMemberToAllOwnerBabies()
    {
        using var factory = NewFactory();
        // 用户 A 创建两个宝宝
        var ownerA = await NewAuthClientAsync(factory, "ownerA_" + Guid.NewGuid().ToString("N")[..6]);
        var baby1Resp = await ownerA.PostAsJsonAsync("/api/baby/add", new CreateBabyRequest { Name = "大宝" });
        var baby1RespBody = await baby1Resp.Content.ReadAsStringAsync();
        Assert.True(baby1Resp.IsSuccessStatusCode, baby1RespBody);
        var baby1Id = (await baby1Resp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetString()!;
        await ownerA.PostAsJsonAsync("/api/baby/add", new CreateBabyRequest { Name = "二宝" });

        // 用户 B 注册
        var userB = await NewAuthClientAsync(factory, "userB_" + Guid.NewGuid().ToString("N")[..6]);

        // B 通过 baby1Id 加入家庭
        var joinResp = await userB.PostAsJsonAsync("/api/baby/family/join", new JoinFamilyRequest
        {
            BabyId = baby1Id,
            RoleCode = "mother",
        });
        var joinRespBody = await joinResp.Content.ReadAsStringAsync();
        Assert.True(joinResp.IsSuccessStatusCode, joinRespBody);

        // B 查看家庭成员，应能看到 A 名下两个宝宝
        var famResp = await userB.GetAsync("/api/baby/family/members");
        var famRespBody = await famResp.Content.ReadAsStringAsync();
        Assert.True(famResp.IsSuccessStatusCode, famRespBody);
        var famBody = await famResp.Content.ReadFromJsonAsync<JsonElement>();
        var families = famBody.GetProperty("data");
        Assert.Equal(2, families.GetArrayLength());
    }

    [Fact]
    public async Task UpdateMyRole_OnlyAffectsSelf()
    {
        using var factory = NewFactory();
        var owner = await NewAuthClientAsync(factory, "roleOwner_" + Guid.NewGuid().ToString("N")[..6]);
        var babyResp = await owner.PostAsJsonAsync("/api/baby/add", new CreateBabyRequest { Name = "宝" });
        var babyRespBody = await babyResp.Content.ReadAsStringAsync();
        Assert.True(babyResp.IsSuccessStatusCode, babyRespBody);
        var babyId = (await babyResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetString()!;

        var member = await NewAuthClientAsync(factory, "roleMember_" + Guid.NewGuid().ToString("N")[..6]);
        var joinResp = await member.PostAsJsonAsync("/api/baby/family/join", new JoinFamilyRequest { BabyId = babyId, RoleCode = "uncle" });
        var joinRespBody = await joinResp.Content.ReadAsStringAsync();
        Assert.True(joinResp.IsSuccessStatusCode, joinRespBody);

        // member 修改自己的角色
        var updResp = await member.PutAsJsonAsync("/api/baby/family/my-role", new UpdateBabyMemberRoleRequest
        {
            BabyId = babyId,
            RoleCode = "grandpa",
        });
        var updRespBody = await updResp.Content.ReadAsStringAsync();
        Assert.True(updResp.IsSuccessStatusCode, updRespBody);
        var updBody = await updResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("爷爷", updBody.GetProperty("data").GetProperty("roleName").GetString());
    }

    [Fact]
    public async Task AddRecord_AndRetrieveToday()
    {
        using var factory = NewFactory();
        var client = await NewAuthClientAsync(factory, "rec_" + Guid.NewGuid().ToString("N")[..6]);
        var babyResp = await client.PostAsJsonAsync("/api/baby/add", new CreateBabyRequest { Name = "宝" });
        Assert.True(babyResp.IsSuccessStatusCode, await babyResp.Content.ReadAsStringAsync());

        var feedResp = await client.PostAsJsonAsync("/api/records/feed", new FeedRecordDto
        {
            Time = DateTime.Now.ToString("O"),
            Type = FeedType.Bottle,
            Amount = 120,
        });
        var feedRespBody = await feedResp.Content.ReadAsStringAsync();
        Assert.True(feedResp.IsSuccessStatusCode, feedRespBody);

        var todayResp = await client.GetAsync("/api/records/today");
        Assert.True(todayResp.IsSuccessStatusCode, await todayResp.Content.ReadAsStringAsync());
        var todayBody = await todayResp.Content.ReadFromJsonAsync<JsonElement>();
        var recordsByType = todayBody.GetProperty("data").GetProperty("recordsByType");
        Assert.True(recordsByType.TryGetProperty("feed", out _));
    }

    [Fact]
    public async Task DeleteRecord_LogicalDelete()
    {
        using var factory = NewFactory();
        var client = await NewAuthClientAsync(factory, "del_" + Guid.NewGuid().ToString("N")[..6]);
        await client.PostAsJsonAsync("/api/baby/add", new CreateBabyRequest { Name = "宝" });
        var feedResp = await client.PostAsJsonAsync("/api/records/feed", new FeedRecordDto
        {
            Time = DateTime.Now.ToString("O"),
            Type = FeedType.Bottle,
            Amount = 100,
        });
        var feedRespBody = await feedResp.Content.ReadAsStringAsync();
        Assert.True(feedResp.IsSuccessStatusCode, feedRespBody);
        var feedBody = await feedResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = feedBody.GetProperty("data").GetProperty("id").GetString()!;

        var delResp = await client.DeleteAsync($"/api/records/{id}");
        Assert.True(delResp.IsSuccessStatusCode, await delResp.Content.ReadAsStringAsync());

        // 再次查询今日记录，feed 应该没有了
        var todayResp = await client.GetAsync("/api/records/today");
        Assert.True(todayResp.IsSuccessStatusCode, await todayResp.Content.ReadAsStringAsync());
        var todayBody = await todayResp.Content.ReadFromJsonAsync<JsonElement>();
        var recordsByType = todayBody.GetProperty("data").GetProperty("recordsByType");
        Assert.False(recordsByType.TryGetProperty("feed", out _));
    }

    [Fact]
    public async Task AccessOtherBaby_Forbidden()
    {
        using var factory = NewFactory();
        var ownerA = await NewAuthClientAsync(factory, "fo_" + Guid.NewGuid().ToString("N")[..6]);
        var babyResp = await ownerA.PostAsJsonAsync("/api/baby/add", new CreateBabyRequest { Name = "宝A" });
        var babyRespBody = await babyResp.Content.ReadAsStringAsync();
        Assert.True(babyResp.IsSuccessStatusCode, babyRespBody);
        var babyAId = (await babyResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetString()!;

        var userB = await NewAuthClientAsync(factory, "fb_" + Guid.NewGuid().ToString("N")[..6]);
        // B 没加入家庭，直接查 A 的宝宝今日记录
        var resp = await userB.GetAsync($"/api/records/today?babyId={babyAId}");
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("000520", body.GetProperty("state").GetString()); // 无权限
    }
}

/// <summary>
/// 测试用 WebApplicationFactory，替换为内存数据库
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    public string DbName { get; } = $"test-{Guid.NewGuid()}";

    /// <summary>测试用 Admin 密码（与 Program.cs 中开发环境回退值解耦）</summary>
    public const string TestAdminPassword = "test-admin-pass-123";

    /// <summary>测试用 Stub EmailSender：捕获最新验证码到邮箱索引（线程安全）</summary>
    private readonly TestEmailSender _emailSender = new();

    /// <summary>获取指定邮箱最后一次发送的验证码明文（仅测试 stub 场景）。</summary>
    public string? GetLastCode(string email) => _emailSender.GetLastCode(email);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            // 测试环境显式覆盖 Admin 密码，避免依赖 appsettings.json 默认值
            services.PostConfigure<Core.Config.AdminOptions>(opt =>
            {
                opt.InitPassword = TestAdminPassword;
            });
            // 测试环境覆盖 EmailAuth：缩短重发间隔，避免 60s 限流影响测试
            services.PostConfigure<Core.Config.EmailAuthOptions>(opt =>
            {
                opt.ResendIntervalSeconds = 0;
                opt.CodeTtlSeconds = 600;
            });
            // 测试环境放宽接口限流：默认 5 req/s 会让"用尽免费次数"类测试（连续 10+ 次调用）触发 429
            services.PostConfigure<Core.Config.RateLimitOptions>(opt =>
            {
                opt.MaxRequestsPerSecond = 1000;
                opt.BlacklistRequestsPerSecond = 2000;
            });
            // 替换 IEmailSender 为测试 Stub，避免真实 SMTP 调用
            services.RemoveAll<Core.Services.IEmailSender>();
            services.AddSingleton<Core.Services.IEmailSender>(_ => _emailSender);
            services.RemoveAll<DbContextOptions<ChildNotesDbContext>>();
            services.RemoveAll<ChildNotesDbContext>();
            services.AddDbContext<ChildNotesDbContext>(opt =>
                opt.UseInMemoryDatabase(DbName));
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ChildNotesDbContext>();
            db.Database.EnsureCreated();
        });
    }
}

/// <summary>
/// 测试用 EmailSender：从 HTML body 中提取 6 位验证码，按邮箱索引保存。
/// 验证码 HTML 模板由 AuthService 生成，含一个 letter-spacing:8px 的 div 包裹纯数字验证码。
/// </summary>
public class TestEmailSender : Core.Services.IEmailSender
{
    private readonly Dictionary<string, string> _codes = new();
    private readonly object _lock = new();

    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        // 从 HTML 中提取 6 位数字验证码（AuthService 模板固定 6 位）
        var match = System.Text.RegularExpressions.Regex.Match(htmlBody, @"\b(\d{6})\b");
        var code = match.Success ? match.Groups[1].Value : "000000";
        lock (_lock)
        {
            _codes[to.Trim().ToLowerInvariant()] = code;
        }
        return Task.CompletedTask;
    }

    public string? GetLastCode(string email)
    {
        lock (_lock)
        {
            return _codes.TryGetValue(email.Trim().ToLowerInvariant(), out var code) ? code : null;
        }
    }
}
