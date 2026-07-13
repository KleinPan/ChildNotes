using ChildNotes.Shared.Constants;

namespace ChildNotes.Shared.Dtos;

/// <summary>
/// 会员套餐定义（前后端共享）。
/// 价格以分为单位（整数），避免浮点精度问题。
/// </summary>
public class MembershipPlanDto
{
    /// <summary>套餐类型标识（monthly/quarterly/yearly）。</summary>
    public string PlanType { get; set; } = string.Empty;

    /// <summary>套餐名称（展示用，如"月卡"、"季卡"、"年卡"）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>套餐时长（天）。</summary>
    public int DurationDays { get; set; }

    /// <summary>价格（单位：分）。如 1800 表示 18.00 元。</summary>
    public int PriceCents { get; set; }

    /// <summary>原始价格（单位：分），用于展示划线价。0 表示不展示划线价。</summary>
    public int OriginalPriceCents { get; set; }

    /// <summary>套餐描述（展示用，如"每月 100 次 AI 分析"）。</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>是否为推荐套餐（前端展示高亮）。</summary>
    public bool IsRecommended { get; set; }

    /// <summary>排序权重（越小越靠前）。</summary>
    public int SortOrder { get; set; }
}

/// <summary>
/// 会员状态响应。
/// </summary>
public class MembershipStatusDto
{
    /// <summary>是否为有效会员。</summary>
    public bool IsActive { get; set; }

    /// <summary>会员到期时间（UTC，ISO 8601 字符串）。非会员为 null。</summary>
    public string? ExpireAt { get; set; }

    /// <summary>今日 AI 记已使用次数。</summary>
    public int AiNoteUsedToday { get; set; }

    /// <summary>今日 AI 记剩余次数。</summary>
    public int AiNoteRemainingToday { get; set; }

    /// <summary>今日 AI 记次数上限。</summary>
    public int AiNoteDailyLimit { get; set; }

    /// <summary>本周 AI 分析已使用次数。</summary>
    public int AiAnalysisUsedThisWeek { get; set; }

    /// <summary>本周 AI 分析剩余次数。</summary>
    public int AiAnalysisRemainingThisWeek { get; set; }

    /// <summary>本周 AI 分析次数上限。</summary>
    public int AiAnalysisWeeklyLimit { get; set; }

    /// <summary>抽奖积分消耗折扣（1 = 原价，0.8 = 8 折）。</summary>
    public decimal LotteryDiscount { get; set; } = 1m;
}

/// <summary>
/// 创建支付订单请求。
/// </summary>
public class CreateOrderRequest
{
    /// <summary>套餐类型（monthly/quarterly/yearly）。</summary>
    public string PlanType { get; set; } = string.Empty;

    /// <summary>支付渠道（alipay）。开发环境可传 mock。</summary>
    public string Channel { get; set; } = MembershipConstants.ChannelAlipay;
}

/// <summary>
/// 创建支付订单响应。
/// </summary>
public class CreateOrderResponse
{
    /// <summary>订单号。</summary>
    public string OrderNo { get; set; } = string.Empty;

    /// <summary>支付渠道。</summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>
    /// 支付参数。支付宝 App 支付时为完整 orderInfo 字符串（前端直接传给 SDK 的 PayTask)。
    /// Mock 模式下为空字符串。
    /// </summary>
    public string PayParams { get; set; } = string.Empty;

    /// <summary>套餐信息。</summary>
    public MembershipPlanDto Plan { get; set; } = new();
}

/// <summary>
/// 查询订单状态响应。
/// </summary>
public class OrderStatusResponse
{
    /// <summary>订单号。</summary>
    public string OrderNo { get; set; } = string.Empty;

    /// <summary>订单状态（pending/paid/closed/refunded）。</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>套餐类型。</summary>
    public string PlanType { get; set; } = string.Empty;

    /// <summary>支付渠道。</summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>创建时间（UTC，ISO 8601 字符串）。</summary>
    public string CreatedAt { get; set; } = string.Empty;

    /// <summary>支付时间（UTC，ISO 8601 字符串）。未支付为 null。</summary>
    public string? PaidAt { get; set; }

    /// <summary>支付成功后的会员状态（仅 paid 状态返回）。</summary>
    public MembershipStatusDto? Membership { get; set; }
}
