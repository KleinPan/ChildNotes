using ChildNotes.Shared.Constants;

namespace ChildNotes.Core.Config;

/// <summary>
/// 会员套餐配置。从 appsettings.json 的 "Membership:Plans" 节点读取。
/// 价格以分为单位（整数），避免浮点精度问题。
/// </summary>
public class MembershipPlanConfig
{
    public string PlanType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int DurationDays { get; set; }
    public int PriceCents { get; set; }
    public int OriginalPriceCents { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool IsRecommended { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>
/// 会员功能配置。支持从配置文件/环境变量动态调整。
/// </summary>
public class MembershipOptions
{
    /// <summary>套餐列表。</summary>
    public List<MembershipPlanConfig> Plans { get; set; } = new();

    /// <summary>普通用户每日免费 AI 分析次数（默认 10）。</summary>
    public int FreeDailyAiLimit { get; set; } = MembershipConstants.FreeDailyAiLimit;

    /// <summary>会员用户每日 AI 分析次数（默认 100）。</summary>
    public int MemberDailyAiLimit { get; set; } = MembershipConstants.MemberDailyAiLimit;

    /// <summary>会员抽奖积分消耗折扣（1 = 原价，0.8 = 8 折）。</summary>
    public decimal MemberLotteryDiscount { get; set; } = MembershipConstants.MemberLotteryDiscount;

    /// <summary>订单超时关闭时间（分钟，默认 30）。</summary>
    public int OrderTimeoutMinutes { get; set; } = 30;

    /// <summary>
    /// 支付宝 App 支付配置。
    /// </summary>
    public AlipayOptions Alipay { get; set; } = new();

    /// <summary>
    /// 是否启用 Mock 模式（开发环境跳过真实支付，直接标记订单为已支付）。
    /// 生产环境必须为 false。
    /// </summary>
    public bool EnableMockPayment { get; set; } = false;
}

/// <summary>
/// 支付宝 App 支付配置。
/// 文档：https://opendocs.alipay.com/open/204/105296/
/// </summary>
public class AlipayOptions
{
    /// <summary>应用 ID（开放平台创建的 App ID）。</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>应用私钥（PKCS8 格式，用于签名）。</summary>
    public string PrivateKey { get; set; } = string.Empty;

    /// <summary>支付宝公钥（用于验签回调通知）。</summary>
    public string AlipayPublicKey { get; set; } = string.Empty;

    /// <summary>回调通知地址（需公网可访问，HTTPS）。</summary>
    public string NotifyUrl { get; set; } = string.Empty;

    /// <summary>
    /// 网关地址。
    /// 正式环境：https://openapi.alipay.com/gateway.do
    /// 沙箱环境：https://openapi-sandbox.dl.alipaydev.com/gateway.do
    /// </summary>
    public string Gateway { get; set; } = "https://openapi.alipay.com/gateway.do";

    /// <summary>签名算法类型（RSA2）。</summary>
    public string SignType { get; set; } = "RSA2";

    /// <summary>销售产品码，App 支付固定为 QUICK_MSECURITY_PAY。</summary>
    public string ProductCode { get; set; } = "QUICK_MSECURITY_PAY";
}
