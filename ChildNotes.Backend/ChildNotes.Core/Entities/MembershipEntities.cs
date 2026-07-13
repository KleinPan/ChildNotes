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
/// AI 调用次数记录。
/// 支持按使用类型区分：
/// - <see cref="MembershipConstants.UsageTypeAiNote"/>：AI 记（按日重置，每日 10/100 次）
/// - <see cref="MembershipConstants.UsageTypeAiAnalysis"/>：AI 分析（按周重置，每周 1/10 次）
///
/// 按 (UserId, UsageType, PeriodStart) 唯一：
/// - AiNote 的 PeriodStart 为当日 UTC 0 点
/// - AiAnalysis 的 PeriodStart 为本周一 UTC 0 点
/// </summary>
public class AiUsageRecord : IAuditable
{
    public string Id { get; set; } = string.Empty;

    /// <summary>用户 ID。</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>使用类型（ai_note / ai_analysis）。</summary>
    public string UsageType { get; set; } = MembershipConstants.UsageTypeAiNote;

    /// <summary>
    /// 周期起始日（UTC，仅日期部分）。
    /// AiNote：当日 0 点；AiAnalysis：本周一 0 点。
    /// </summary>
    public DateTime PeriodStart { get; set; }

    /// <summary>当前周期内已使用次数。</summary>
    public int UsedCount { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
