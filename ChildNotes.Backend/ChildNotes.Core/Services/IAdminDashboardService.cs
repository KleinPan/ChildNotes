using ChildNotes.Core.Dtos;

namespace ChildNotes.Core.Services;

public interface IAdminDashboardService
{
    Task<AdminOverviewResponse> GetOverviewAsync(CancellationToken ct = default);
    Task<AdminPageResponse<AdminUserListItemDto>> ListUsersAsync(int page, int pageSize, string? keyword, CancellationToken ct = default);
    Task<AdminPageResponse<AdminBabyListItemDto>> ListBabiesAsync(int page, int pageSize, string? keyword, CancellationToken ct = default);
}
