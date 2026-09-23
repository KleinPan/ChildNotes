using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ChildNotes.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ChildNotes.Tests;

/// <summary>
/// Mock 支付闭环与渠道互斥回归（资损防护 #16）：
/// - Mock 闭环：mock 渠道下单后订单直接标记 paid 并激活会员（前端空 PayParams → 轮询即见 paid）
/// - 渠道门禁：EnableMockPayment=false 时 mock 渠道 400 拒绝（生产无资损面）
/// - 渠道解耦：EnableMockPayment=true 时 alipay 渠道仍走真实 orderInfo（订单保持 pending），
///   不被 mock 开关静默降级为断裂路径
/// </summary>
public class MockPaymentTests : IDisposable
{
    private readonly RSA _rsa = RSA.Create(2048);

    private ApiFactory NewFactory(bool enableMock) => new()
    {
        ConfigureMembershipOptions = opt => opt.EnableMockPayment = enableMock,
    };

    /// <summary>注册登录并携带 JWT。</summary>
    private async Task<HttpClient> LoginAsync(ApiFactory factory)
    {
        var email = $"mock_{Guid.NewGuid():N}@test.local";
        var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/send-code", new Core.Dtos.SendCodeRequest { Email = email });
        resp.EnsureSuccessStatusCode();
        var code = factory.GetLastCode(email)!;
        var verify = await client.PostAsJsonAsync("/api/auth/verify-code",
            new Core.Dtos.VerifyCodeRequest { Email = email, Code = code });
        verify.EnsureSuccessStatusCode();
        var token = (await verify.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    private async Task<(string OrderNo, string PayParams)> CreateOrderAsync(
        HttpClient client, string channel)
    {
        var resp = await client.PostAsJsonAsync("/api/membership/orders",
            new { planType = "monthly", channel });
        var body = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, body);
        var json = (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        return (json.GetProperty("orderNo").GetString()!,
                json.GetProperty("payParams").GetString() ?? string.Empty);
    }

    private async Task<(string Status, DateTime? ExpireAt)> GetOrderAndExpiryAsync(
        ApiFactory factory, string orderNo)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChildNotesDbContext>();
        var order = await db.MembershipOrders.FirstAsync(o => o.OrderNo == orderNo);
        var expireAt = await db.AppUsers.Where(u => u.Id == order.UserId).Select(u => u.MembershipExpireAt).FirstAsync();
        return (order.Status, expireAt);
    }

    [Fact]
    public async Task MockOrder_AutoPaid_ActivatesMembership()
    {
        using var factory = NewFactory(enableMock: true);
        var client = await LoginAsync(factory);

        var (orderNo, payParams) = await CreateOrderAsync(client, "mock");
        Assert.Equal(string.Empty, payParams); // 前端契约：空 PayParams = mock 模式

        // 轮询即见 paid，且会员已激活（缺陷 A：修复前订单永远 pending）
        var (status, expireAt) = await GetOrderAndExpiryAsync(factory, orderNo);
        Assert.Equal(Shared.Constants.MembershipConstants.OrderStatusPaid, status);
        Assert.NotNull(expireAt);
        var days = (expireAt!.Value - DateTime.UtcNow).TotalDays;
        Assert.InRange(days, 29, 31); // 月卡 30 天
    }

    [Fact]
    public async Task MockChannel_Disabled_Rejected()
    {
        using var factory = NewFactory(enableMock: false);
        var client = await LoginAsync(factory);

        var resp = await client.PostAsJsonAsync("/api/membership/orders",
            new { planType = "monthly", channel = "mock" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("MOCK_DISABLED", body);
    }

    [Fact]
    public async Task AlipayChannel_StillPending_WhenMockEnabled()
    {
        using var factory = new ApiFactory
        {
            ConfigureMembershipOptions = opt =>
            {
                opt.EnableMockPayment = true;
                // alipay 渠道需要真实可用的私钥（BuildOrderInfo 会用私钥签名，
                // 无效 Base64/密钥会在签名阶段抛异常）。复用测试 RSA 密钥对。
                opt.Alipay.AppId = "test-app-id";
                opt.Alipay.PrivateKey = Convert.ToBase64String(_rsa.ExportPkcs8PrivateKey());
            },
        };
        var client = await LoginAsync(factory);

        // 缺陷 B：mock 开启时 alipay 渠道不得被降级为"空 PayParams + 自动 paid"
        var (orderNo, payParams) = await CreateOrderAsync(client, "alipay");
        Assert.NotEqual(string.Empty, payParams);

        var (status, expireAt) = await GetOrderAndExpiryAsync(factory, orderNo);
        Assert.Equal(Shared.Constants.MembershipConstants.OrderStatusPending, status);
        Assert.Null(expireAt);
    }

    public void Dispose() => _rsa.Dispose();
}
