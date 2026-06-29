using ChildNotes.Core.Constants;
using ChildNotes.Core.Dtos;
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
    public async Task Register_ReturnsTokenAndUser()
    {
        using var factory = NewFactory();
        var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Username = "user_reg_" + Guid.NewGuid().ToString("N")[..6],
            Password = "pass123",
            NickName = "Reg",
        });
        Assert.True(resp.IsSuccessStatusCode, await resp.Content.ReadAsStringAsync());
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("000000", body.GetProperty("state").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("data").GetProperty("token").GetString()));
        Assert.True(body.GetProperty("data").GetProperty("newUser").GetBoolean());
    }

    [Fact]
    public async Task Login_WithWrongPassword_Fails()
    {
        using var factory = NewFactory();
        var client = factory.CreateClient();
        var username = "user_login_" + Guid.NewGuid().ToString("N")[..6];
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest { Username = username, Password = "pass123" });
        var resp = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Username = username, Password = "wrong" });
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
            .GetProperty("data").GetProperty("id").GetInt64();
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
            .GetProperty("data").GetProperty("id").GetInt64();

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
        var id = feedBody.GetProperty("data").GetProperty("id").GetInt64();

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
            .GetProperty("data").GetProperty("id").GetInt64();

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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
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
