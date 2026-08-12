using ChildNotes.Core.Dtos;
using ChildNotes.Shared.Dtos;

namespace ChildNotes.Core.Services;

public interface IBabyService
{
    Task<BabyDto?> GetCurrentBabyAsync(string? babyId, CancellationToken ct = default);
    Task<List<BabyDto>> ListBabiesAsync(CancellationToken ct = default);
    Task<BabyDto> CreateBabyAsync(CreateBabyRequest req, CancellationToken ct = default);
    Task<BabyDto> UpdateBabyAsync(UpdateBabyRequest req, CancellationToken ct = default);
    Task<List<BabyFamilyDto>> ListFamilyMembersAsync(CancellationToken ct = default);
    Task<BabyMemberDto> UpdateMyFamilyRoleAsync(UpdateBabyMemberRoleRequest req, CancellationToken ct = default);
    Task<BabyMemberDto> JoinFamilyViaInviteAsync(JoinFamilyRequest req, CancellationToken ct = default);

    /// <summary>owner 移除家庭成员（软删除 BabyMember，Status=removed）。</summary>
    Task RemoveMemberAsync(RemoveMemberRequest req, CancellationToken ct = default);

    /// <summary>提交加入家庭申请（创建 pending 的 FamilyJoinRequest）。</summary>
    Task<FamilyJoinRequestDto> RequestJoinAsync(JoinFamilyRequest req, CancellationToken ct = default);

    /// <summary>owner 列出自己所有宝宝下待审的加入申请。</summary>
    Task<List<FamilyJoinRequestDto>> ListPendingJoinRequestsAsync(CancellationToken ct = default);

    /// <summary>当前用户作为申请人，列出自己的加入申请（用于查看状态）。</summary>
    Task<List<FamilyJoinRequestDto>> ListMyJoinRequestsAsync(CancellationToken ct = default);

    /// <summary>owner 批准或拒绝加入申请。</summary>
    Task<FamilyJoinRequestDto> ProcessJoinRequestAsync(ProcessJoinRequestDto req, CancellationToken ct = default);
}
