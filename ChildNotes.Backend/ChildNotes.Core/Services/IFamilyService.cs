using ChildNotes.Core.Dtos;

namespace ChildNotes.Core.Services;

/// <summary>
/// 家庭解析服务（Family-centric 模型的"当前家庭"唯一解析点）。
/// 详见 docs/development/family-identity-architecture.md。
/// MVP 单家庭：多家庭用户取 CreatedAt 最早的家庭作为当前家庭（阶段 4 引入切换 UI 后再扩展）。
/// </summary>
public interface IFamilyService
{
    /// <summary>用户所属家庭列表（按 CreatedAt 排序，首个即当前家庭）。登录响应与权限查询共用。</summary>
    Task<List<FamilyDto>> GetUserFamiliesAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// 用户当前家庭 Id。无任何家庭时返回 null（登录/注册已保证有默认家庭，null 属异常态，调用方需防御）。
    /// </summary>
    Task<string?> GetCurrentFamilyIdAsync(string userId, CancellationToken ct = default);
}
