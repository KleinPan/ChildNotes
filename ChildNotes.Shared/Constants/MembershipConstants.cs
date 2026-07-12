namespace ChildNotes.Shared.Constants;

/// <summary>
/// 会员相关常量（前后端共享）。
/// 套餐类型用字符串标识，便于扩展（如未来增加"连续包月"等）。
/// </summary>
public static class MembershipConstants
{
    /// <summary>普通用户每日免费 AI 分析次数。</summary>
    public const int FreeDailyAiLimit = 10;

    /// <summary>会员用户每日 AI 分析次数。</summary>
    public const int MemberDailyAiLimit = 100;

    /// <summary>会员抽奖积分消耗折扣（1 = 原价，0.8 = 8 折）。</summary>
    public const decimal MemberLotteryDiscount = 0.8m;

    /// <summary>套餐类型：月卡。</summary>
    public const string PlanMonthly = "monthly";

    /// <summary>套餐类型：季卡。</summary>
    public const string PlanQuarterly = "quarterly";

    /// <summary>套餐类型：年卡。</summary>
    public const string PlanYearly = "yearly";

    /// <summary>订单状态：待支付。</summary>
    public const string OrderStatusPending = "pending";

    /// <summary>订单状态：已支付。</summary>
    public const string OrderStatusPaid = "paid";

    /// <summary>订单状态：已关闭（超时/取消）。</summary>
    public const string OrderStatusClosed = "closed";

    /// <summary>订单状态：已退款。</summary>
    public const string OrderStatusRefunded = "refunded";

    /// <summary>支付渠道：支付宝。</summary>
    public const string ChannelAlipay = "alipay";

    /// <summary>支付渠道：Mock（开发环境模拟）。</summary>
    public const string ChannelMock = "mock";

    /// <summary>所有支持的套餐类型列表（顺序即展示顺序）。</summary>
    public static readonly IReadOnlyList<string> AllPlans = new[] { PlanMonthly, PlanQuarterly, PlanYearly };

    /// <summary>套餐时长（天）。月卡 30 天、季卡 90 天、年卡 365 天。</summary>
    public static int GetPlanDurationDays(string planType) => planType switch
    {
        PlanMonthly => 30,
        PlanQuarterly => 90,
        PlanYearly => 365,
        _ => 0,
    };

    /// <summary>判断用户是否为会员（到期时间晚于当前 UTC 时间）。</summary>
    public static bool IsActive(DateTime? membershipExpireAt)
        => membershipExpireAt.HasValue && membershipExpireAt.Value > DateTime.UtcNow;
}
