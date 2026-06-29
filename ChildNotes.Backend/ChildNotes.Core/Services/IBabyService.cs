using ChildNotes.Core.Dtos;
using ChildNotes.Shared.Dtos;

namespace ChildNotes.Core.Services;

public interface IBabyService
{
    Task<BabyDto?> GetCurrentBabyAsync(long? babyId, CancellationToken ct = default);
    Task<List<BabyDto>> ListBabiesAsync(CancellationToken ct = default);
    Task<BabyDto> CreateBabyAsync(CreateBabyRequest req, CancellationToken ct = default);
    Task<BabyDto> UpdateBabyAsync(UpdateBabyRequest req, CancellationToken ct = default);
    Task<List<BabyFamilyDto>> ListFamilyMembersAsync(CancellationToken ct = default);
    Task<BabyMemberDto> UpdateMyFamilyRoleAsync(UpdateBabyMemberRoleRequest req, CancellationToken ct = default);
    Task<BabyMemberDto> JoinFamilyViaInviteAsync(JoinFamilyRequest req, CancellationToken ct = default);
}
