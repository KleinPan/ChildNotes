using ChildNotes.Core.Config;
using ChildNotes.Core.Entities;
using ChildNotes.Core.Exceptions;
using ChildNotes.Core.Services;
using ChildNotes.Infrastructure.Data;
using ChildNotes.Infrastructure.External;
using ChildNotes.Shared.Constants;
using ChildNotes.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChildNotes.Infrastructure.Services;

/// <summary>
/// 会员服务实现：套餐查询、会员状态、订单创建、支付回调、AI 次数管理。
/// </summary>
public class MembershipService : IMembershipService
{
    private readonly ChildNotesDbContext _db;
    private readonly ICurrentUserService _current;
    private readonly MembershipOptions _opt;

    public MembershipService(ChildNotesDbContext db, ICurrentUserService current, IOptions<MembershipOptions> opt)
    {
        _db = db;
        _current = current;
        _opt = opt.Value;
    }

    public Task<List<MembershipPlanDto>> GetPlansAsync(CancellationToken ct = default)
    {
        var list = _opt.Plans
            .OrderBy(p => p.SortOrder)
            .Select(p => new MembershipPlanDto
            {
                PlanType = p.PlanType,
                Name = p.Name,
                DurationDays = p.DurationDays,
                PriceCents = p.PriceCents,
                OriginalPriceCents = p.OriginalPriceCents,
                Description = p.Description,
                IsRecommended = p.IsRecommended,
                SortOrder = p.SortOrder,
            })
            .ToList();
        return Task.FromResult(list);
    }

    public async Task<MembershipStatusDto> GetStatusAsync(CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        return await BuildStatusAsync(uid, ct);
    }

    public async Task<CreateOrderResponse> CreateOrderAsync(CreateOrderRequest req, CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        var plan = _opt.Plans.FirstOrDefault(p => p.PlanType == req.PlanType)
            ?? throw new BusinessException($"套餐不存在：{req.PlanType}", 400, "PLAN_NOT_FOUND");

        var channel = string.IsNullOrEmpty(req.Channel) ? MembershipConstants.ChannelAlipay : req.Channel;

        // Mock 模式：仅开发环境允许
        if (channel == MembershipConstants.ChannelMock && !_opt.EnableMockPayment)
            throw new BusinessException("Mock 支付未启用", 400, "MOCK_DISABLED");

        // 支付宝支付必须配置凭证
        if (channel == MembershipConstants.ChannelAlipay && !_opt.EnableMockPayment)
        {
            if (string.IsNullOrEmpty(_opt.Alipay.AppId) || string.IsNullOrEmpty(_opt.Alipay.PrivateKey))
                throw new BusinessException("支付宝未配置", 500, "ALIPAY_NOT_CONFIGURED");
        }

        var orderNo = GenerateOrderNo();
        var order = new MembershipOrder
        {
            Id = Guid.NewGuid().ToString("N"),
            OrderNo = orderNo,
            UserId = uid,
            PlanType = plan.PlanType,
            PlanName = plan.Name,
            DurationDays = plan.DurationDays,
            PriceCents = plan.PriceCents,
            Channel = channel,
            Status = MembershipConstants.OrderStatusPending,
        };
        _db.MembershipOrders.Add(order);
        await _db.SaveChangesAsync(ct);

        var planDto = new MembershipPlanDto
        {
            PlanType = plan.PlanType,
            Name = plan.Name,
            DurationDays = plan.DurationDays,
            PriceCents = plan.PriceCents,
            OriginalPriceCents = plan.OriginalPriceCents,
            Description = plan.Description,
            IsRecommended = plan.IsRecommended,
            SortOrder = plan.SortOrder,
        };

        // 生成支付参数
        var payParams = string.Empty;
        if (channel == MembershipConstants.ChannelAlipay && !_opt.EnableMockPayment)
        {
            var client = new AlipayAppPayClient(_opt.Alipay);
            var totalAmount = (plan.PriceCents / 100m).ToString("0.00");
            var subject = $"ChildNotes会员-{plan.Name}";
            payParams = client.BuildOrderInfo(orderNo, totalAmount, subject);
        }
        // Mock 模式：直接返回空串，前端收到空串后模拟支付成功

        return new CreateOrderResponse
        {
            OrderNo = orderNo,
            Channel = channel,
            PayParams = payParams,
            Plan = planDto,
        };
    }

    public async Task<OrderStatusResponse> GetOrderStatusAsync(string orderNo, CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        var order = await _db.MembershipOrders.FirstOrDefaultAsync(o => o.OrderNo == orderNo && o.UserId == uid, ct)
            ?? throw new NotFoundException("订单不存在");

        var resp = new OrderStatusResponse
        {
            OrderNo = order.OrderNo,
            Status = order.Status,
            PlanType = order.PlanType,
            Channel = order.Channel,
            CreatedAt = order.CreatedAt.ToString("O"),
            PaidAt = order.PaidAt?.ToString("O"),
        };

        if (order.Status == MembershipConstants.OrderStatusPaid)
        {
            resp.Membership = await BuildStatusAsync(uid, ct);
        }
        return resp;
    }

    public async Task<string> HandleAlipayNotifyAsync(IDictionary<string, string> form, CancellationToken ct = default)
    {
        // 复制一份避免修改原始字典
        var dict = new Dictionary<string, string>(form);

        // 验签
        var sign = dict.GetValueOrDefault("sign") ?? string.Empty;
        var client = new AlipayAppPayClient(_opt.Alipay);
        if (!client.VerifyNotifySign(dict, sign))
        {
            // 验签失败，不处理
            return "fail";
        }

        var tradeStatus = dict.GetValueOrDefault("trade_status");
        var outTradeNo = dict.GetValueOrDefault("out_trade_no");
        var tradeNo = dict.GetValueOrDefault("trade_no");

        if (string.IsNullOrEmpty(outTradeNo))
            return "fail";

        var order = await _db.MembershipOrders.FirstOrDefaultAsync(o => o.OrderNo == outTradeNo, ct);
        if (order is null)
            return "fail";

        // 已处理的订单直接返回 success（支付宝会重复通知）
        if (order.Status == MembershipConstants.OrderStatusPaid)
            return "success";

        // 仅处理交易成功
        if (tradeStatus != "TRADE_SUCCESS" && tradeStatus != "TRADE_FINISHED")
            return "success";

        // 事务：更新订单 + 延长会员
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            order.Status = MembershipConstants.OrderStatusPaid;
            order.TradeNo = tradeNo;
            order.PaidAt = DateTime.UtcNow;
            order.CallbackPayload = System.Text.Json.JsonSerializer.Serialize(dict);
            order.UpdatedAt = DateTime.UtcNow;

            await ActivateMembershipAsync(order.UserId, order.DurationDays, ct);
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return "success";
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<int> GetAiDailyLimitAsync(string userId, CancellationToken ct = default)
    {
        var user = await _db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return _opt.FreeDailyAiLimit;
        return MembershipConstants.IsActive(user.MembershipExpireAt)
            ? _opt.MemberDailyAiLimit
            : _opt.FreeDailyAiLimit;
    }

    public async Task<int> IncrementAiUsageAsync(string userId, CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.Date;

        // 尝试原子递增（PostgreSQL 支持 ExecuteUpdateAsync）
        if (_db.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory")
        {
            var rows = await _db.AiUsageRecords
                .Where(x => x.UserId == userId && x.UsageDate == today)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.UsedCount, x => x.UsedCount + 1)
                    .SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct);

            if (rows > 0)
            {
                return await _db.AiUsageRecords
                    .Where(x => x.UserId == userId && x.UsageDate == today)
                    .Select(x => x.UsedCount)
                    .FirstAsync(ct);
            }
        }

        // 首次或 InMemory：插入新记录（幂等处理）
        var existing = await _db.AiUsageRecords.FirstOrDefaultAsync(x => x.UserId == userId && x.UsageDate == today, ct);
        if (existing is not null)
        {
            existing.UsedCount++;
            existing.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return existing.UsedCount;
        }

        var record = new AiUsageRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            UsageDate = today,
            UsedCount = 1,
        };
        _db.AiUsageRecords.Add(record);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // 并发竞态：重新递增
            existing = await _db.AiUsageRecords.FirstAsync(x => x.UserId == userId && x.UsageDate == today, ct);
            existing.UsedCount++;
            await _db.SaveChangesAsync(ct);
            return existing.UsedCount;
        }
        return 1;
    }

    public async Task<int> GetAiUsedTodayAsync(string userId, CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.Date;
        var record = await _db.AiUsageRecords.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId && x.UsageDate == today, ct);
        return record?.UsedCount ?? 0;
    }

    public async Task<decimal> GetLotteryDiscountAsync(string userId, CancellationToken ct = default)
    {
        var user = await _db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return 1m;
        return MembershipConstants.IsActive(user.MembershipExpireAt) ? _opt.MemberLotteryDiscount : 1m;
    }

    /// <summary>
    /// 激活/延长会员资格。
    /// 若当前已是会员，从当前到期时间往后延长；否则从当前时间往后延长。
    /// </summary>
    private async Task ActivateMembershipAsync(string userId, int durationDays, CancellationToken ct)
    {
        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return;

        var now = DateTime.UtcNow;
        var baseTime = (user.MembershipExpireAt ?? now) > now
            ? user.MembershipExpireAt!.Value
            : now;
        user.MembershipExpireAt = baseTime.AddDays(durationDays);
        user.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);
    }

    private async Task<MembershipStatusDto> BuildStatusAsync(string userId, CancellationToken ct)
    {
        var user = await _db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new UnauthorizedException();

        var isActive = MembershipConstants.IsActive(user.MembershipExpireAt);
        var limit = isActive ? _opt.MemberDailyAiLimit : _opt.FreeDailyAiLimit;
        var used = await GetAiUsedTodayAsync(userId, ct);

        return new MembershipStatusDto
        {
            IsActive = isActive,
            ExpireAt = user.MembershipExpireAt?.ToString("O"),
            AiUsedToday = used,
            AiRemainingToday = limit - used,
            AiDailyLimit = limit,
            LotteryDiscount = isActive ? _opt.MemberLotteryDiscount : 1m,
        };
    }

    /// <summary>
    /// 生成订单号：年月日(8位) + 6位随机字符。总长 14 位。
    /// </summary>
    private static string GenerateOrderNo()
    {
        var date = DateTime.UtcNow.ToString("yyyyMMdd");
        var random = Guid.NewGuid().ToString("N")[..6];
        return $"{date}{random}";
    }
}
