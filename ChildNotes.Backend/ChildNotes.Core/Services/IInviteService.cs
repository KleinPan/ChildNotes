using ChildNotes.Core.Dtos;

namespace ChildNotes.Core.Services;

public interface IInviteService
{
    Task<List<InviteRecordDto>> GetInviteRecordsAsync(CancellationToken ct = default);
    Task BindReferrerAsync(string userId, string referrerId, bool newUser, CancellationToken ct = default);
}
