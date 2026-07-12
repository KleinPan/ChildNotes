using ChildNotes.Shared.Dtos;

namespace ChildNotes.Core.Services;

/// <summary>
/// 会员服务：套餐查询、会员状态查询、订单创建、支付回调处理。
/// </summary>
public interface IMembershipService
{
    /// <summary>获取所有可用套餐（按 SortOrder 排序）。</summary>
    Task<List<MembershipPlanDto>> GetPlansAsync(CancellationToken ct = default);

    /// <summary>获取当前用户的会员状态。</summary>
    Task<MembershipStatusDto> GetStatusAsync(CancellationToken ct = default);

    /// <summary>创建支付订单，返回支付参数（支付宝 orderInfo 或 Mock 空串）。</summary>
    Task<CreateOrderResponse> CreateOrderAsync(CreateOrderRequest req, CancellationToken ct = default);

    /// <summary>查询订单状态（支付完成后轮询用）。</summary>
    Task<OrderStatusResponse> GetOrderStatusAsync(string orderNo, CancellationToken ct = default);

    /// <summary>
    /// 支付宝异步回调处理。返回 "success" 表示处理成功，其他值支付宝会重试。
    /// </summary>
    Task<string> HandleAlipayNotifyAsync(IDictionary<string, string> form, CancellationToken ct = default);

    /// <summary>
    /// 获取当前用户的每日 AI 次数限制（根据会员状态决定）。
    /// </summary>
    Task<int> GetAiDailyLimitAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// 增加用户今日 AI 调用次数（+1）。幂等命中不调用。
    /// 返回增加后的已用次数。
    /// </summary>
    Task<int> IncrementAiUsageAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// 获取用户今日已用 AI 次数。
    /// </summary>
    Task<int> GetAiUsedTodayAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// 获取会员抽奖折扣。非会员返回 1（原价）。
    /// </summary>
    Task<decimal> GetLotteryDiscountAsync(string userId, CancellationToken ct = default);
}
