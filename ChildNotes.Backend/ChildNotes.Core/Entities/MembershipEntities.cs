using ChildNotes.Core.Constants;
using ChildNotes.Shared.Constants;

namespace ChildNotes.Core.Entities;

/// <summary>
/// 会员订单实体。记录用户购买会员的支付订单。
/// 订单号格式：年月日 + 6 位随机字符（如 20260712ab3d9f）。
/// </summary>
public class MembershipOrder : IAuditable
{
    public string Id { get; set; } = string.Empty;

    /// <summary>订单号（用户可见，用于支付与回调）。</summary>
    public string OrderNo { get; set; } = string.Empty;

    /// <summary>用户 ID。</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>套餐类型（monthly/quarterly/yearly）。</summary>
    public string PlanType { get; set; } = string.Empty;

    /// <summary>套餐名称（下单时的快照，如"月卡"）。</summary>
    public string PlanName { get; set; } = string.Empty;

    /// <summary>套餐时长（天，下单时的快照）。</summary>
    public int DurationDays { get; set; }

    /// <summary>价格（单位：分，下单时的快照）。</summary>
    public int PriceCents { get; set; }

    /// <summary>支付渠道（alipay/mock）。</summary>
    public string Channel { get; set; } = MembershipConstants.ChannelAlipay;

    /// <summary>订单状态（pending/paid/closed/refunded）。</summary>
    public string Status { get; set; } = MembershipConstants.OrderStatusPending;

    /// <summary>支付宝交易号（支付成功后由回调填入）。</summary>
    public string? TradeNo { get; set; }

    /// <summary>支付时间（UTC）。未支付为 null。</summary>
    public DateTime? PaidAt { get; set; }

    /// <summary>回调原始内容（JSON，用于对账追溯）。</summary>
    public string? CallbackPayload { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// AI 分析每日调用次数记录。
/// 按 (UserId, Date) 唯一，每次实际调用 AI 分析（非幂等命中）时 +1。
/// </summary>
public class AiUsageRecord : IAuditable
{
    public string Id { get; set; } = string.Empty;

    /// <summary>用户 ID。</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>日期（UTC，仅日期部分，时间固定 00:00:00）。</summary>
    public DateTime UsageDate { get; set; }

    /// <summary>当日已使用次数。</summary>
    public int UsedCount { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
