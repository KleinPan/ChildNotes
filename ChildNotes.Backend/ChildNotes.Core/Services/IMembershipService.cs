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
    /// 获取当前用户的 AI 记每日次数限制（根据会员状态决定）。
    /// </summary>
    Task<int> GetAiNoteDailyLimitAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// 增加用户今日 AI 记调用次数（+1）。
    /// 返回增加后的已用次数。
    /// </summary>
    Task<int> IncrementAiNoteUsageAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// 原子地检查额度并递增今日 AI 记调用次数。
    /// 仅在未超限时 +1，返回 (是否成功, 递增后已用次数)。
    /// 解决"先 SELECT 检查 + 后 UPDATE 递增"的 TOCTOU 并发漏洞。
    /// </summary>
    Task<(bool ok, int used)> TryIncrementAiNoteUsageAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// 强制递增今日 AI 记调用次数（+1，允许 UsedCount 超过当日限额）。
    /// 仅供"免费次数用尽后积分抵扣"场景使用：扣抵扣积分成功后放行本次调用。
    /// </summary>
    Task<int> ForceIncrementAiNoteUsageAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// 获取用户今日 AI 记已用次数。
    /// </summary>
    Task<int> GetAiNoteUsedTodayAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// 获取当前用户的 AI 分析每周次数限制（根据会员状态决定）。
    /// </summary>
    Task<int> GetAiAnalysisWeeklyLimitAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// 增加用户本周 AI 分析调用次数（+1）。
    /// 返回增加后的已用次数。
    /// </summary>
    Task<int> IncrementAiAnalysisUsageAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// 原子地检查额度并递增本周 AI 分析调用次数。
    /// 仅在未超限时 +1，返回 (是否成功, 递增后已用次数)。
    /// 解决"先 SELECT 检查 + 后 UPDATE 递增"的 TOCTOU 并发漏洞。
    /// </summary>
    Task<(bool ok, int used)> TryIncrementAiAnalysisUsageAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// 强制递增本周 AI 分析调用次数（+1，允许 UsedCount 超过本周限额）。
    /// 仅供"免费次数用尽后积分抵扣"场景使用：扣抵扣积分成功后放行本次调用。
    /// </summary>
    Task<int> ForceIncrementAiAnalysisUsageAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// 递减本周 AI 分析调用次数（-1，不低于 0）。用于 AI 调用失败时退还次数。
    /// </summary>
    Task DecrementAiAnalysisUsageAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// 获取用户本周 AI 分析已用次数。
    /// </summary>
    Task<int> GetAiAnalysisUsedThisWeekAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// 获取会员抽奖折扣。非会员返回 1（原价）。
    /// </summary>
    Task<decimal> GetLotteryDiscountAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// 为当前用户激活永不过期会员（开发版 APK 调用）。
    /// 调用方需自行通过 MembershipOptions.EnableDevAutoActivate 控制端点暴露。
    /// </summary>
    Task DevActivatePermanentAsync(CancellationToken ct = default);
}
