using ChildNotes.Core.Dtos;
using ChildNotes.Core.Entities;

namespace ChildNotes.Core.Services;

public interface IAdminAuthService
{
    Task<AdminLoginResponse> LoginAsync(AdminLoginRequest req, CancellationToken ct = default);
    Task<AdminAccount?> AuthenticateAsync(string? token, CancellationToken ct = default);
    Task<AdminAccount?> GetCurrentAdminAsync(CancellationToken ct = default);
    Task LogoutAsync(CancellationToken ct = default);
    Task EnsureDefaultAdminAsync(CancellationToken ct = default);
}
