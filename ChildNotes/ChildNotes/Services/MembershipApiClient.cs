using System.Net.Http;
using System.Text.Json;
using ChildNotes.Data.Repositories;
using ChildNotes.Infrastructure;
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

    /// <summary>
    /// 【开发版】激活永不过期会员。仅 DEV_BUILD 构建调用。
    /// 后端需开启 EnableDevAutoActivate，否则返回 404（静默忽略）。
    /// </summary>
    public async Task<bool> DevActivatePermanentAsync(CancellationToken ct = default)
    {
        try
        {
            var cfg = _cfgRepo.Get();
            DevLogger.Log("Membership", $"[DevActivate] 开始：ServerUrl={cfg.ServerUrl ?? "(空)"}, Token长度={cfg.Token?.Length ?? 0}");
            if (string.IsNullOrWhiteSpace(cfg.ServerUrl))
            {
                DevLogger.Log("Membership", "[DevActivate] 跳过：ServerUrl 未配置", DevLogger.Level.Warn);
                return false;
            }
            using var resp = await SendAsync(_cfgRepo, HttpMethod.Post, "/api/membership/dev/activate", null, ct);
            if (resp is null)
            {
                DevLogger.Log("Membership", "[DevActivate] 失败：SendAsync 返回 null（token 未配置/自动登录失败/网络异常）", DevLogger.Level.Warn);
                return false;
            }
            var body = resp.IsSuccessStatusCode ? "(成功)" : await resp.Content.ReadAsStringAsync(ct);
            DevLogger.Log("Membership", $"[DevActivate] 完成：StatusCode={resp.StatusCode}, Body={body}");
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            DevLogger.Log("Membership", $"[DevActivate] 异常：{ex.GetType().Name}: {ex.Message}", DevLogger.Level.Error);
            return false;
        }
    }
}
