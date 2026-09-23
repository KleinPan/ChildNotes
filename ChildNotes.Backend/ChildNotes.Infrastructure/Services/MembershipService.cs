using ChildNotes.Core.Config;
using ChildNotes.Core.Entities;
using ChildNotes.Core.Exceptions;
using ChildNotes.Core.Services;
using ChildNotes.Infrastructure.Data;
using ChildNotes.Infrastructure.External;
using ChildNotes.Shared.Constants;
using ChildNotes.Shared.Dtos;
using ChildNotes.Shared.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
    private readonly AlipayAppPayClient _alipay;
    private readonly Microsoft.Extensions.Logging.ILogger<MembershipService> _logger;

    public MembershipService(ChildNotesDbContext db, ICurrentUserService current,
        IOptions<MembershipOptions> opt, AlipayAppPayClient alipay,
        ILogger<MembershipService> logger)
    {
        _db = db;
        _current = current;
        _opt = opt.Value;
        _alipay = alipay;
        _logger = logger;
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

        // 支付宝支付必须配置凭证。
        // 注意：判断只看渠道本身，不叠加 EnableMockPayment——否则 mock 开关一开，
        // alipay 真实订单会静默降级为"无凭证 + 空 PayParams"的断裂路径（缺陷 B 修复）。
        if (channel == MembershipConstants.ChannelAlipay)
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

        // 生成支付参数（渠道判断只看渠道本身，与 EnableMockPayment 解耦）
        var payParams = string.Empty;
        if (channel == MembershipConstants.ChannelAlipay)
        {
            var totalAmount = (plan.PriceCents / 100m).ToString("0.00");
            var subject = $"ChildNotes会员-{plan.Name}";
            payParams = _alipay.BuildOrderInfo(orderNo, totalAmount, subject);
        }
        else if (channel == MembershipConstants.ChannelMock)
        {
            // Mock 闭环（缺陷 A 修复）：开发环境直接标记订单已支付并激活会员。
            // 前端契约：收到空 PayParams 后轮询订单状态，此处保证轮询即见 paid。
            // 生产环境 EnableMockPayment=false，mock 渠道在上方已被 400 拒绝，无资损面。
            order.Status = MembershipConstants.OrderStatusPaid;
            order.TradeNo = $"mock-{orderNo}";
            order.PaidAt = DateTime.UtcNow;
            order.CallbackPayload = System.Text.Json.JsonSerializer.Serialize(
                new Dictionary<string, string> { ["mock"] = "true" });
            order.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            await ActivateMembershipAsync(uid, order.DurationDays, ct);
        }

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
        if (!_alipay.VerifyNotifySign(dict, sign))
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

        // 金额校验（资损防线）：回调金额必须与下单快照一致，防止低价成交/篡改开通。
        // 不一致返回 "fail" 让支付宝重发通知（便于人工对账），订单保持 pending 不激活。
        // app_id 校验：回调必须属于本应用的订单（seller_id 未配置，暂不校验）。
        if (!decimal.TryParse(dict.GetValueOrDefault("total_amount"),
                System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var totalAmount))
        {
            _logger.LogWarning("alipay notify: invalid/missing total_amount, order={OrderNo}", outTradeNo);
            return "fail";
        }
        if ((int)decimal.Round(totalAmount * 100, MidpointRounding.ToEven) != order.PriceCents)
        {
            _logger.LogWarning(
                "alipay notify: amount mismatch, order={OrderNo}, expect={ExpectCents}cents, notify={NotifyAmount}yuan — kept pending for reconciliation",
                outTradeNo, order.PriceCents, totalAmount);
            return "fail";
        }
        var notifyAppId = dict.GetValueOrDefault("app_id");
        if (!string.IsNullOrEmpty(notifyAppId) && !string.IsNullOrEmpty(_opt.Alipay.AppId) && notifyAppId != _opt.Alipay.AppId)
        {
            _logger.LogWarning("alipay notify: app_id mismatch, order={OrderNo}, notify={NotifyAppId}", outTradeNo, notifyAppId);
            return "fail";
        }

        // 事务：原子抢占订单（WHERE status=pending）+ 延长会员。
        // 幂等修复：原实现"先读 Status 再事务内全量赋值"，支付宝并发重发通知时两个事务
        // 都能读到 pending，各自延长一次会员（时长翻倍）；且两笔不同订单并发激活会
        // last-write-wins 丢一笔延期。现改为条件更新抢占：只有 pending → paid 的更新
        // 影响行数 > 0 才执行激活，并发重发时后到者 rows==0 直接幂等返回 success。
        // ExecuteInTransactionAsync：InMemory（测试环境）自动降级为无事务执行。
        await _db.ExecuteInTransactionAsync(async () =>
        {
            // InMemory 不支持 ExecuteUpdateAsync，降级为 EF 跟踪 + 状态复查（测试环境无并发）
            if (_db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
            {
                if (order.Status != MembershipConstants.OrderStatusPending)
                    return; // 并发已处理，幂等
                order.Status = MembershipConstants.OrderStatusPaid;
                order.TradeNo = tradeNo;
                order.PaidAt = DateTime.UtcNow;
                order.CallbackPayload = System.Text.Json.JsonSerializer.Serialize(dict);
                order.UpdatedAt = DateTime.UtcNow;
                await ActivateMembershipAsync(order.UserId, order.DurationDays, ct);
                return;
            }

            var now = DateTime.UtcNow;
            var claimed = await _db.MembershipOrders
                .Where(o => o.Id == order.Id && o.Status == MembershipConstants.OrderStatusPending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, MembershipConstants.OrderStatusPaid)
                    .SetProperty(x => x.TradeNo, tradeNo)
                    .SetProperty(x => x.PaidAt, now)
                    .SetProperty(x => x.CallbackPayload, System.Text.Json.JsonSerializer.Serialize(dict))
                    .SetProperty(x => x.UpdatedAt, now), ct);
            if (claimed == 0)
                return; // 另一并发通知已处理该订单：幂等成功，不重复激活

            await ActivateMembershipAsync(order.UserId, order.DurationDays, ct);
        }, ct);
        // 幂等语义：无论本请求是否执行激活（含并发已处理 rows==0），对支付宝都返回 success 停止重发
        return "success";
    }

    public async Task<int> GetAiNoteDailyLimitAsync(string userId, CancellationToken ct = default)
    {
        var user = await _db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return _opt.FreeDailyAiNoteLimit;
        return MembershipConstants.IsActive(user.MembershipExpireAt)
            ? _opt.MemberDailyAiNoteLimit
            : _opt.FreeDailyAiNoteLimit;
    }

    public Task<int> IncrementAiNoteUsageAsync(string userId, CancellationToken ct = default)
        => IncrementUsageAsync(userId, MembershipConstants.UsageTypeAiNote, ChinaToday, ct);

    public async Task<(bool ok, int used)> TryIncrementAiNoteUsageAsync(string userId, CancellationToken ct = default)
    {
        var limit = await GetAiNoteDailyLimitAsync(userId, ct);
        return await TryIncrementUsageAsync(userId, MembershipConstants.UsageTypeAiNote, ChinaToday, limit, ct);
    }

    /// <summary>强制递增（允许超限），供积分抵扣放行场景使用。</summary>
    public Task<int> ForceIncrementAiNoteUsageAsync(string userId, CancellationToken ct = default)
        => IncrementUsageAsync(userId, MembershipConstants.UsageTypeAiNote, ChinaToday, ct);

    public Task<int> GetAiNoteUsedTodayAsync(string userId, CancellationToken ct = default)
        => GetUsedAsync(userId, MembershipConstants.UsageTypeAiNote, ChinaToday, ct);

    public Task DecrementAiNoteUsageAsync(string userId, CancellationToken ct = default)
        => DecrementUsageAsync(userId, MembershipConstants.UsageTypeAiNote, ChinaToday, ct);

    public async Task<int> GetAiAnalysisWeeklyLimitAsync(string userId, CancellationToken ct = default)
    {
        var user = await _db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return _opt.FreeWeeklyAiAnalysisLimit;
        return MembershipConstants.IsActive(user.MembershipExpireAt)
            ? _opt.MemberWeeklyAiAnalysisLimit
            : _opt.FreeWeeklyAiAnalysisLimit;
    }

    public Task<int> IncrementAiAnalysisUsageAsync(string userId, CancellationToken ct = default)
        => IncrementUsageAsync(userId, MembershipConstants.UsageTypeAiAnalysis, GetWeekStartUtc(ChinaToday), ct);

    public async Task<(bool ok, int used)> TryIncrementAiAnalysisUsageAsync(string userId, CancellationToken ct = default)
    {
        var limit = await GetAiAnalysisWeeklyLimitAsync(userId, ct);
        return await TryIncrementUsageAsync(userId, MembershipConstants.UsageTypeAiAnalysis, GetWeekStartUtc(ChinaToday), limit, ct);
    }

    /// <summary>强制递增（允许超限），供积分抵扣放行场景使用。</summary>
    public Task<int> ForceIncrementAiAnalysisUsageAsync(string userId, CancellationToken ct = default)
        => IncrementUsageAsync(userId, MembershipConstants.UsageTypeAiAnalysis, GetWeekStartUtc(ChinaToday), ct);

    public Task DecrementAiAnalysisUsageAsync(string userId, CancellationToken ct = default)
        => DecrementUsageAsync(userId, MembershipConstants.UsageTypeAiAnalysis, GetWeekStartUtc(ChinaToday), ct);

    public Task<int> GetAiAnalysisUsedThisWeekAsync(string userId, CancellationToken ct = default)
        => GetUsedAsync(userId, MembershipConstants.UsageTypeAiAnalysis, GetWeekStartUtc(ChinaToday), ct);

    public async Task<decimal> GetLotteryDiscountAsync(string userId, CancellationToken ct = default)
    {
        var user = await _db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return 1m;
        return MembershipConstants.IsActive(user.MembershipExpireAt) ? _opt.MemberLotteryDiscount : 1m;
    }

    /// <summary>
    /// 为当前用户激活永不过期会员。
    /// 直接将 MembershipExpireAt 设为 DateTime.MaxValue，开发版 APK 登录后自动调用。
    /// </summary>
    public async Task DevActivatePermanentAsync(CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Id == uid, ct);
        if (user is null) return;

        // 已是永久会员则跳过
        if (user.MembershipExpireAt == DateTime.MaxValue) return;

        user.MembershipExpireAt = DateTime.MaxValue;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// 通用次数递增逻辑。按 (userId, usageType, periodStart) 唯一约束。
    /// PostgreSQL 走 ExecuteUpdateAsync 原子递增；InMemory 走 EF 跟踪。
    /// </summary>
    private async Task<int> IncrementUsageAsync(string userId, string usageType, DateTime periodStart, CancellationToken ct)
    {
        // 尝试原子递增（PostgreSQL 支持 ExecuteUpdateAsync）
        if (_db.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory")
        {
            var rows = await _db.AiUsageRecords
                .Where(x => x.UserId == userId && x.UsageType == usageType && x.PeriodStart == periodStart)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.UsedCount, x => x.UsedCount + 1)
                    .SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct);

            if (rows > 0)
            {
                return await _db.AiUsageRecords
                    .Where(x => x.UserId == userId && x.UsageType == usageType && x.PeriodStart == periodStart)
                    .Select(x => x.UsedCount)
                    .FirstAsync(ct);
            }
        }

        // 首次或 InMemory：插入新记录（幂等处理）
        var existing = await _db.AiUsageRecords.FirstOrDefaultAsync(
            x => x.UserId == userId && x.UsageType == usageType && x.PeriodStart == periodStart, ct);
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
            UsageType = usageType,
            PeriodStart = periodStart,
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
            existing = await _db.AiUsageRecords.FirstAsync(
                x => x.UserId == userId && x.UsageType == usageType && x.PeriodStart == periodStart, ct);
            existing.UsedCount++;
            await _db.SaveChangesAsync(ct);
            return existing.UsedCount;
        }
        return 1;
    }

    /// <summary>
    /// 通用次数递减逻辑（-1，不低于 0）。
    /// PostgreSQL 走 ExecuteUpdateAsync 原子递减；InMemory 走 EF 跟踪（测试环境无并发）。
    /// </summary>
    private async Task DecrementUsageAsync(string userId, string usageType, DateTime periodStart, CancellationToken ct)
    {
        // InMemory 不支持 ExecuteUpdateAsync，降级到 EF 跟踪模式（测试环境无并发）
        if (_db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
        {
            var record = await _db.AiUsageRecords.FirstOrDefaultAsync(
                x => x.UserId == userId && x.UsageType == usageType && x.PeriodStart == periodStart, ct);
            if (record is null || record.UsedCount <= 0) return;
            record.UsedCount--;
            record.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return;
        }

        // 原子递减，不低于 0
        await _db.AiUsageRecords
            .Where(x => x.UserId == userId && x.UsageType == usageType
                && x.PeriodStart == periodStart && x.UsedCount > 0)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.UsedCount, x => x.UsedCount - 1)
                .SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct);
    }

    /// <summary>
    /// 原子地检查额度并递增。WHERE UsedCount &lt; limit 保证并发安全：
    /// 多个请求同时到达时，只有未超限的请求能 +1，超限的返回 (false, currentUsed)。
    /// 首次使用（无记录）时直接插入 UsedCount=1，若 limit=0 则不插入返回 (false, 0)。
    /// PostgreSQL 走 ExecuteUpdateAsync 原子递增；InMemory 走 EF 跟踪（测试环境无并发）。
    /// </summary>
    private async Task<(bool ok, int used)> TryIncrementUsageAsync(
        string userId, string usageType, DateTime periodStart, int limit, CancellationToken ct)
    {
        if (limit <= 0)
            return (false, 0);

        // InMemory 不支持 ExecuteUpdateAsync，降级到 EF 跟踪模式（测试环境无并发）
        if (_db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
        {
            var record = await _db.AiUsageRecords.FirstOrDefaultAsync(
                x => x.UserId == userId && x.UsageType == usageType && x.PeriodStart == periodStart, ct);
            if (record is null)
            {
                record = new AiUsageRecord
                {
                    Id = Guid.NewGuid().ToString("N"),
                    UserId = userId,
                    UsageType = usageType,
                    PeriodStart = periodStart,
                    UsedCount = 1,
                };
                _db.AiUsageRecords.Add(record);
                await _db.SaveChangesAsync(ct);
                return (true, 1);
            }
            if (record.UsedCount < limit)
            {
                record.UsedCount++;
                record.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                return (true, record.UsedCount);
            }
            return (false, record.UsedCount);
        }

        // PostgreSQL：原子条件递增
        var rows = await _db.AiUsageRecords
            .Where(x => x.UserId == userId && x.UsageType == usageType && x.PeriodStart == periodStart
                && x.UsedCount < limit)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.UsedCount, x => x.UsedCount + 1)
                .SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct);

        if (rows > 0)
        {
            var used = await _db.AiUsageRecords
                .Where(x => x.UserId == userId && x.UsageType == usageType && x.PeriodStart == periodStart)
                .Select(x => x.UsedCount)
                .FirstAsync(ct);
            return (true, used);
        }

        // rows==0：要么记录不存在（首次），要么已超限
        var existing = await _db.AiUsageRecords.AsNoTracking().FirstOrDefaultAsync(
            x => x.UserId == userId && x.UsageType == usageType && x.PeriodStart == periodStart, ct);
        if (existing is not null)
            return (false, existing.UsedCount); // 已超限

        // 首次使用，插入新记录（并发竞态时另一请求已插入，改为条件递增）
        var record2 = new AiUsageRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            UsageType = usageType,
            PeriodStart = periodStart,
            UsedCount = 1,
        };
        _db.AiUsageRecords.Add(record2);
        try
        {
            await _db.SaveChangesAsync(ct);
            return (true, 1);
        }
        catch (DbUpdateException)
        {
            rows = await _db.AiUsageRecords
                .Where(x => x.UserId == userId && x.UsageType == usageType && x.PeriodStart == periodStart
                    && x.UsedCount < limit)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.UsedCount, x => x.UsedCount + 1)
                    .SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct);
            var used = await _db.AiUsageRecords
                .Where(x => x.UserId == userId && x.UsageType == usageType && x.PeriodStart == periodStart)
                .Select(x => x.UsedCount)
                .FirstAsync(ct);
            return (rows > 0, used);
        }
    }

    private async Task<int> GetUsedAsync(string userId, string usageType, DateTime periodStart, CancellationToken ct)
    {
        var record = await _db.AiUsageRecords.AsNoTracking().FirstOrDefaultAsync(
            x => x.UserId == userId && x.UsageType == usageType && x.PeriodStart == periodStart, ct);
        return record?.UsedCount ?? 0;
    }

    /// <summary>
    /// 北京时间的"业务今天"（#11）：AI 日配额按北京时间自然日重置。
    /// 返回 Kind=Utc 的北京墙钟日期（如北京时间 9-23 存为 2026-09-23T00:00Z），
    /// 满足 Npgsql 写 timestamptz 的 Kind 要求，且与 RecordDate 存储口径一致。
    /// </summary>
    private static DateTime ChinaToday => DateTime.SpecifyKind(ChinaTime.Today, DateTimeKind.Utc);

    /// <summary>
    /// 获取本周一的 UTC 0 点（按自然周计算，周一为一周起始）。
    /// 入参为 Kind=Utc 的北京墙钟日期，返回值保持 Kind=Utc（周起点同样按北京日切分）。
    /// </summary>
    private static DateTime GetWeekStartUtc(DateTime dt)
    {
        var d = dt.Date;
        // DayOfWeek: Sunday=0, Monday=1, ..., Saturday=6
        // 转换为"距本周一的天数"
        int diff = d.DayOfWeek == DayOfWeek.Sunday ? 6 : (int)d.DayOfWeek - 1;
        return d.AddDays(-diff);
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

        var noteLimit = isActive ? _opt.MemberDailyAiNoteLimit : _opt.FreeDailyAiNoteLimit;
        var noteUsed = await GetAiNoteUsedTodayAsync(userId, ct);

        var analysisLimit = isActive ? _opt.MemberWeeklyAiAnalysisLimit : _opt.FreeWeeklyAiAnalysisLimit;
        var analysisUsed = await GetAiAnalysisUsedThisWeekAsync(userId, ct);

        return new MembershipStatusDto
        {
            IsActive = isActive,
            ExpireAt = user.MembershipExpireAt?.ToString("O"),
            AiNoteUsedToday = noteUsed,
            // 抵扣放行后 UsedCount 可能超过 limit，剩余次数按下限 0 返回，避免前端展示负数
            AiNoteRemainingToday = Math.Max(0, noteLimit - noteUsed),
            AiNoteDailyLimit = noteLimit,
            AiAnalysisUsedThisWeek = analysisUsed,
            AiAnalysisRemainingThisWeek = Math.Max(0, analysisLimit - analysisUsed),
            AiAnalysisWeeklyLimit = analysisLimit,
            LotteryDiscount = isActive ? _opt.MemberLotteryDiscount : 1m,
            // 超限积分抵扣单价（前端弹窗展示用，值取 Shared 常量保证前后端一致）
            AiNoteOveragePointsCost = MembershipConstants.AiNoteOveragePointsCost,
            AiAnalysisOveragePointsCost = MembershipConstants.AiAnalysisOveragePointsCost,
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
