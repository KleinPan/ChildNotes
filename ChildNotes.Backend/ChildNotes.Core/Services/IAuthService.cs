using ChildNotes.Core.Dtos;

namespace ChildNotes.Core.Services;

public interface IAuthService
{
    Task<LoginResponse> RegisterAsync(RegisterRequest req, CancellationToken ct = default);
    Task<LoginResponse> LoginAsync(LoginRequest req, CancellationToken ct = default);
    Task<LoginUserDto> GetCurrentUserAsync(CancellationToken ct = default);
    Task<LoginUserDto> UpdateProfileAsync(UpdateProfileRequest req, CancellationToken ct = default);
}
