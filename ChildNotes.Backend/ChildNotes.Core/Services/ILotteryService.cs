using ChildNotes.Core.Dtos;

namespace ChildNotes.Core.Services;

public interface ILotteryService
{
    Task<LotterySummaryDto?> GetActiveLotteryAsync(CancellationToken ct = default);
    /// <summary>
    /// 参与抽奖并扣减积分。不返回 Dashboard——由调用方（Controller）随后调用
    /// <see cref="IPointsService.GetDashboardAsync"/> 以保持 HTTP 响应契约。
    /// </summary>
    Task JoinLotteryAsync(long activityId, CancellationToken ct = default);
    Task<List<LotteryHistoryItemDto>> GetLotteryHistoryAsync(CancellationToken ct = default);
}
