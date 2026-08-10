using ChildNotes.Core.Dtos;

namespace ChildNotes.Core.Services;

public interface IPointsService
{
    Task<PointsDashboardResponse> GetDashboardAsync(CancellationToken ct = default);

    /// <summary>获取任务列表（含完成/领取状态）。</summary>
    Task<List<TaskTemplateDto>> GetTasksAsync(CancellationToken ct = default);

    /// <summary>
    /// 领取任务奖励。任务未完成或已领取会抛 <see cref="Exceptions.BusinessException"/>。
    /// </summary>
    /// <param name="taskKey">任务 key（如 daily_record）。</param>
    /// <returns>领取结果（含本次奖励和最新余额）。</returns>
    Task<ClaimTaskResponse> ClaimTaskAsync(string taskKey, CancellationToken ct = default);
}
