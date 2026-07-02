using ChildNotes.Shared.Dtos;

namespace ChildNotes.Core.Services;

/// <summary>
/// 成长时刻（里程碑）服务接口。对齐小程序端 /api/records/milestone 系列 API。
/// </summary>
public interface IMilestoneService
{
    /// <summary>查询当前用户可访问宝宝的所有里程碑（按日期倒序）。</summary>
    Task<List<MilestoneRecordDto>> ListAsync(string? babyId, CancellationToken ct = default);

    /// <summary>新增里程碑。返回新记录 Id。</summary>
    Task<string> AddAsync(MilestoneRecordDto dto, CancellationToken ct = default);

    /// <summary>更新里程碑。返回是否实际更新（找不到返回 false）。</summary>
    Task<bool> UpdateAsync(string id, MilestoneRecordDto dto, CancellationToken ct = default);

    /// <summary>软删里程碑。</summary>
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);
}
