using ChildNotes.Core.Entities;

namespace ChildNotes.Core.Services;

/// <summary>
/// 宝宝访问权限校验服务：统一封装"用户对宝宝的访问权"判断，
/// 消除 AiAnalysisService/BabyService/RecordService/SyncService 中的重复实现。
/// 访问权定义：用户是宝宝创建者，或为该宝宝 active 成员。
/// </summary>
public interface IBabyAccessService
{
    /// <summary>用户是否可访问指定宝宝。</summary>
    Task<bool> HasAccessAsync(string userId, string babyId, CancellationToken ct = default);

    /// <summary>确保用户可访问指定宝宝，否则抛 ForbiddenException。</summary>
    Task EnsureAccessAsync(string userId, string babyId, CancellationToken ct = default);

    /// <summary>查询用户可访问的所有宝宝 ID 集合（自己创建 + active 成员）。</summary>
    Task<List<string>> GetAccessibleBabyIdsAsync(string userId, CancellationToken ct = default);

    /// <summary>查询用户可访问的所有宝宝实体。</summary>
    Task<List<Baby>> GetAccessibleBabiesAsync(string userId, CancellationToken ct = default);

    /// <summary>获取用户默认宝宝（按 Id 升序的第一个可访问宝宝）。</summary>
    Task<Baby?> GetDefaultBabyAsync(string userId, CancellationToken ct = default);
}
