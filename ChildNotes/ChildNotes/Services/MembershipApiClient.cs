using System.Net.Http;
using System.Text.Json;
using ChildNotes.Data.Repositories;
using ChildNotes.Shared.Dtos;

namespace ChildNotes.Services;

/// <summary>
/// 会员 API 客户端：调用后端 /api/membership/* 接口。
/// 套餐查询、会员状态、订单创建、订单状态轮询。
/// </summary>
public sealed class MembershipApiClient : BaseApiClient
{
    private readonly SyncConfigRepository _cfgRepo;

    public MembershipApiClient(SyncConfigRepository cfgRepo) => _cfgRepo = cfgRepo;

    /// <summary>获取所有可用套餐。</summary>
    public async Task<List<MembershipPlanDto>?> GetPlansAsync(CancellationToken ct = default)
    {
        using var resp = await SendAsync(_cfgRepo, HttpMethod.Get, "/api/membership/plans", null, ct);
        return resp is null ? null : await ReadDataAsync<List<MembershipPlanDto>>(resp, ct);
    }

    /// <summary>获取当前用户的会员状态。</summary>
    public async Task<MembershipStatusDto?> GetStatusAsync(CancellationToken ct = default)
    {
        using var resp = await SendAsync(_cfgRepo, HttpMethod.Get, "/api/membership/status", null, ct);
        return resp is null ? null : await ReadDataAsync<MembershipStatusDto>(resp, ct);
    }

    /// <summary>创建支付订单。</summary>
    public async Task<CreateOrderResponse?> CreateOrderAsync(string planType, string channel, CancellationToken ct = default)
    {
        var body = Serialize(new { PlanType = planType, Channel = channel });
        using var resp = await SendAsync(_cfgRepo, HttpMethod.Post, "/api/membership/orders", body, ct);
        return resp is null ? null : await ReadDataAsync<CreateOrderResponse>(resp, ct);
    }

    /// <summary>查询订单状态（支付完成后轮询）。</summary>
    public async Task<OrderStatusResponse?> GetOrderStatusAsync(string orderNo, CancellationToken ct = default)
    {
        var path = $"/api/membership/orders/{Uri.EscapeDataString(orderNo)}";
        using var resp = await SendAsync(_cfgRepo, HttpMethod.Get, path, null, ct);
        return resp is null ? null : await ReadDataAsync<OrderStatusResponse>(resp, ct);
    }
}
