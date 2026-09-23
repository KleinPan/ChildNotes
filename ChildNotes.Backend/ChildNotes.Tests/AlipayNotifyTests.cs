using System.Formats.Asn1;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using ChildNotes.Infrastructure.Data;
using ChildNotes.Infrastructure.External;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace ChildNotes.Tests;

/// <summary>
/// 支付宝回调幂等与金额校验回归（资损防护 #4）：
/// - 回调金额必须与订单快照一致（防低价成交），不符返回 fail 且不激活
/// - 重复通知幂等：已 paid 订单再通知不重复延长会员（防并发/重发时长翻倍）
/// - 服务端条件抢占（WHERE status=pending）+ ActivateMembership 同事务
/// 测试用自生成 RSA 密钥对模拟支付宝侧签名（私钥签名、公钥配到服务端验签）。
/// </summary>
public class AlipayNotifyTests : IDisposable
{
    private readonly RSA _rsa = RSA.Create(2048);

    private ApiFactory NewFactory() => new()
    {
        ConfigureMembershipOptions = opt =>
        {
            // 同一对 RSA：PrivateKey 用于服务端签 orderInfo（创建订单需有效私钥），
            // AlipayPublicKey 用于回调验签（测试私钥签名 → 服务端公钥验签通过）。
            opt.Alipay.AppId = "test-app-id";
            opt.Alipay.PrivateKey = Convert.ToBase64String(_rsa.ExportPkcs8PrivateKey());
            opt.Alipay.AlipayPublicKey = Convert.ToBase64String(_rsa.ExportSubjectPublicKeyInfo());
        },
    };

    /// <summary>登录并用测试私钥构建签名，模拟支付宝服务器回调。</summary>
    private async Task<(HttpClient client, string orderNo, int priceCents)> CreateOrderAsync(ApiFactory factory)
    {
        var email = $"alipay_{Guid.NewGuid():N}@test.local";
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

        var orderResp = await client.PostAsJsonAsync("/api/membership/orders",
            new { planType = "monthly", channel = "alipay" });
        var orderBody = await orderResp.Content.ReadAsStringAsync();
        Assert.True(orderResp.IsSuccessStatusCode, orderBody);
        var orderNo = (await orderResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("orderNo").GetString()!;

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChildNotesDbContext>();
        var priceCents = await db.MembershipOrders.Where(o => o.OrderNo == orderNo).Select(o => o.PriceCents).FirstAsync();
        return (client, orderNo, priceCents);
    }

    /// <summary>用测试私钥对回调内容签名（模拟支付宝服务器）。</summary>
    private string Sign(IDictionary<string, string> dict)
    {
        var content = AlipaySignature.BuildSignContent(dict);
        var dataBytes = Encoding.UTF8.GetBytes(content);
        var sig = _rsa.SignData(dataBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return Convert.ToBase64String(sig);
    }

    private Dictionary<string, string> BuildNotify(string orderNo, int priceCents,
        string tradeStatus = "TRADE_SUCCESS", string? amountOverride = null)
    {
        var amount = amountOverride ?? (priceCents / 100m).ToString("0.00");
        var dict = new Dictionary<string, string>
        {
            ["app_id"] = "test-app-id",
            ["trade_status"] = tradeStatus,
            ["out_trade_no"] = orderNo,
            ["trade_no"] = "2026" + Guid.NewGuid().ToString("N")[..20],
            ["total_amount"] = amount,
            ["sign_type"] = "RSA2",
        };
        dict["sign"] = Sign(dict);
        return dict;
    }

    private async Task<string> NotifyAsync(ApiFactory factory, Dictionary<string, string> dict)
    {
        var client = factory.CreateClient(); // 匿名（支付宝服务器角色）
        var resp = await client.PostAsync("/api/membership/alipay/notify",
            new FormUrlEncodedContent(dict));
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync();
    }

    private async Task<(string Status, DateTime? ExpireAt)> GetOrderAndExpiryAsync(ApiFactory factory, string orderNo)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChildNotesDbContext>();
        var order = await db.MembershipOrders.FirstAsync(o => o.OrderNo == orderNo);
        var expireAt = await db.AppUsers.Where(u => u.Id == order.UserId).Select(u => u.MembershipExpireAt).FirstAsync();
        return (order.Status, expireAt);
    }

    [Fact]
    public async Task ValidNotify_ActivatesMembership()
    {
        using var factory = NewFactory();
        var (_, orderNo, priceCents) = await CreateOrderAsync(factory);

        var result = await NotifyAsync(factory, BuildNotify(orderNo, priceCents));
        Assert.Equal("success", result);

        var (status, expireAt) = await GetOrderAndExpiryAsync(factory, orderNo);
        Assert.Equal(Shared.Constants.MembershipConstants.OrderStatusPaid, status);
        Assert.NotNull(expireAt);
        // 月卡 30 天：到期时间在 now+29~31 天内
        var days = (expireAt!.Value - DateTime.UtcNow).TotalDays;
        Assert.InRange(days, 29, 31);
    }

    [Fact]
    public async Task AmountMismatch_KeepsPending_ReturnsFail()
    {
        using var factory = NewFactory();
        var (_, orderNo, priceCents) = await CreateOrderAsync(factory);

        // 篡改金额（低价成交攻击）：签名仍有效，但金额与订单快照不符
        var result = await NotifyAsync(factory, BuildNotify(orderNo, priceCents, amountOverride: "0.01"));
        Assert.Equal("fail", result);

        var (status, expireAt) = await GetOrderAndExpiryAsync(factory, orderNo);
        Assert.Equal(Shared.Constants.MembershipConstants.OrderStatusPending, status); // 未激活
        Assert.Null(expireAt);
    }

    [Fact]
    public async Task DuplicateNotify_IsIdempotent_DoesNotExtendTwice()
    {
        using var factory = NewFactory();
        var (_, orderNo, priceCents) = await CreateOrderAsync(factory);

        var first = await NotifyAsync(factory, BuildNotify(orderNo, priceCents));
        Assert.Equal("success", first);
        var (_, expireAt1) = await GetOrderAndExpiryAsync(factory, orderNo);
        Assert.NotNull(expireAt1);

        // 支付宝重复重发同一通知：再次成功返回，但会员时间不得再次延长
        var second = await NotifyAsync(factory, BuildNotify(orderNo, priceCents));
        Assert.Equal("success", second);
        var (_, expireAt2) = await GetOrderAndExpiryAsync(factory, orderNo);
        Assert.Equal(expireAt1, expireAt2); // 幂等：时长未翻倍
    }

    [Fact]
    public async Task Notify_WithoutAmount_Fails()
    {
        using var factory = NewFactory();
        var (_, orderNo, priceCents) = await CreateOrderAsync(factory);

        // 缺失 total_amount：无法校验金额，拒绝
        var dict = BuildNotify(orderNo, priceCents);
        dict.Remove("total_amount");
        dict["sign"] = Sign(dict); // 重新签名（移除字段后内容变化）
        var result = await NotifyAsync(factory, dict);
        Assert.Equal("fail", result);

        var (status, _) = await GetOrderAndExpiryAsync(factory, orderNo);
        Assert.Equal(Shared.Constants.MembershipConstants.OrderStatusPending, status);
    }

    public void Dispose() => _rsa.Dispose();
}
