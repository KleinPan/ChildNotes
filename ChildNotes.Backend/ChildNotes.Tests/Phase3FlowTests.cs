using System.Net.Http.Json;
using System.Text.Json;
using ChildNotes.Core.Dtos;
using ChildNotes.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ChildNotes.Tests;

public class Phase3FlowTests
{
    private static ApiFactory NewFactory() => new();
    private const string AdminPassword = "change-this-admin-password";

    private static async Task<HttpClient> NewAdminClientAsync(ApiFactory factory)
    {
        using (var scope = factory.Services.CreateScope())
        {
            var auth = scope.ServiceProvider.GetRequiredService<Core.Services.IAdminAuthService>();
            await auth.EnsureDefaultAdminAsync();
        }
        var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/admin/api/auth/login", new AdminLoginRequest
        {
            Username = "admin",
            Password = AdminPassword,
        });
        var body = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, body);
        var json = JsonDocument.Parse(body).RootElement.GetProperty("data");
        var token = json.GetProperty("token").GetString()!;
        Assert.False(string.IsNullOrEmpty(token));
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    private static async Task RegisterUserAsync(ApiFactory factory, string username)
    {
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Username = username,
            Password = "pass123",
            NickName = username + "-nick",
        });
    }

    private static string? GetState(JsonElement body) => body.GetProperty("state").GetString();
    private static JsonElement GetData(JsonElement body) => body.GetProperty("data");

    [Fact]
    public async Task AdminLogin_DefaultAdmin_Works()
    {
        using var factory = NewFactory();
        using (var scope = factory.Services.CreateScope())
        {
            var auth = scope.ServiceProvider.GetRequiredService<Core.Services.IAdminAuthService>();
            await auth.EnsureDefaultAdminAsync();
        }
        var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/admin/api/auth/login", new AdminLoginRequest
        {
            Username = "admin",
            Password = AdminPassword,
        });
        var body = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, body);
        var data = JsonDocument.Parse(body).RootElement.GetProperty("data");
        Assert.Equal("admin", data.GetProperty("username").GetString());
        Assert.False(string.IsNullOrEmpty(data.GetProperty("token").GetString()));
    }

    [Fact]
    public async Task AdminLogin_WrongPassword_Fails()
    {
        using var factory = NewFactory();
        using (var scope = factory.Services.CreateScope())
        {
            var auth = scope.ServiceProvider.GetRequiredService<Core.Services.IAdminAuthService>();
            await auth.EnsureDefaultAdminAsync();
        }
        var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/admin/api/auth/login", new AdminLoginRequest
        {
            Username = "admin",
            Password = "wrong-password",
        });
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("000520", GetState(body));
    }

    [Fact]
    public async Task AdminEndpoints_RequireAuth()
    {
        using var factory = NewFactory();
        var client = factory.CreateClient();
        var resp = await client.GetAsync("/admin/api/overview");
        Assert.Equal(401, (int)resp.StatusCode);
    }

    [Fact]
    public async Task Overview_ReturnsCounts()
    {
        using var factory = NewFactory();
        await RegisterUserAsync(factory, "u1_" + Guid.NewGuid().ToString("N")[..6]);
        await RegisterUserAsync(factory, "u2_" + Guid.NewGuid().ToString("N")[..6]);
        var client = await NewAdminClientAsync(factory);

        var resp = await client.GetAsync("/admin/api/overview");
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("000000", GetState(body));
        var data = GetData(body);
        Assert.True(data.GetProperty("totalUsers").GetInt64() >= 2);
    }

    [Fact]
    public async Task ListUsers_Paginates()
    {
        using var factory = NewFactory();
        for (int i = 0; i < 3; i++)
            await RegisterUserAsync(factory, $"p{i}_" + Guid.NewGuid().ToString("N")[..6]);
        var client = await NewAdminClientAsync(factory);

        var resp = await client.GetAsync("/admin/api/users?page=1&pageSize=2");
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var data = GetData(body);
        Assert.Equal(2, data.GetProperty("pageSize").GetInt32());
        Assert.True(data.GetProperty("total").GetInt64() >= 3);
        Assert.Equal(2, data.GetProperty("records").GetArrayLength());
    }

    [Fact]
    public async Task LotteryCrud_CreatePublishClose()
    {
        using var factory = NewFactory();
        var client = await NewAdminClientAsync(factory);

        var create = new AdminLotteryRequest
        {
            Title = "测试抽奖",
            Description = "desc",
            StartTime = DateTime.UtcNow.AddDays(1),
            DrawTime = DateTime.UtcNow.AddDays(7),
            CostPoints = 30,
            WinnerCount = 1,
            Status = "draft",
            Prizes = new() { new() { PrizeName = "奖品A", PrizeCount = 1 } },
        };
        var resp = await client.PostAsJsonAsync("/admin/api/lotteries", create);
        var respBody = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, respBody);
        var body = JsonDocument.Parse(respBody).RootElement;
        var data = GetData(body);
        var id = data.GetProperty("id").GetInt64();
        Assert.Equal("draft", data.GetProperty("status").GetString());

        var pubResp = await client.PostAsync($"/admin/api/lotteries/{id}/publish", null);
        var pubBody = await pubResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("published", GetData(pubBody).GetProperty("status").GetString());

        var closeResp = await client.PostAsync($"/admin/api/lotteries/{id}/close", null);
        var closeBody = await closeResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("closed", GetData(closeBody).GetProperty("status").GetString());
    }

    [Fact]
    public async Task LotteryPublish_WithoutPrize_Fails()
    {
        using var factory = NewFactory();
        var client = await NewAdminClientAsync(factory);

        var create = new AdminLotteryRequest
        {
            Title = "无奖品",
            StartTime = DateTime.UtcNow.AddDays(1),
            DrawTime = DateTime.UtcNow.AddDays(7),
            Status = "draft",
            Prizes = new(),
        };
        var resp = await client.PostAsJsonAsync("/admin/api/lotteries", create);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var id = GetData(body).GetProperty("id").GetInt64();

        var pubResp = await client.PostAsync($"/admin/api/lotteries/{id}/publish", null);
        var pubBody = await pubResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("000520", GetState(pubBody));
        Assert.Contains("at least one prize", pubBody.GetProperty("msg").GetString()!);
    }

    [Fact]
    public async Task Logout_ClearsToken()
    {
        using var factory = NewFactory();
        var client = await NewAdminClientAsync(factory);

        var resp = await client.PostAsync("/admin/api/auth/logout", null);
        Assert.True(resp.IsSuccessStatusCode);

        // 用旧 token 访问应失败
        var afterResp = await client.GetAsync("/admin/api/overview");
        Assert.Equal(401, (int)afterResp.StatusCode);
    }
}
