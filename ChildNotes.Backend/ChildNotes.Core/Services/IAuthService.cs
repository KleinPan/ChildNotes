using ChildNotes.Core.Dtos;

namespace ChildNotes.Core.Services;

public interface IAuthService
{
    Task<SendCodeResponse> SendCodeAsync(SendCodeRequest req, CancellationToken ct = default);
    Task<AuthResponse> VerifyCodeAsync(VerifyCodeRequest req, CancellationToken ct = default);
    Task<AuthResponse> RefreshAsync(RefreshRequest req, CancellationToken ct = default);
    Task<LoginUserDto> GetCurrentUserAsync(CancellationToken ct = default);
    Task<LoginUserDto> UpdateProfileAsync(UpdateProfileRequest req, CancellationToken ct = default);
}

public class SendCodeResponse
{
    public bool Sent { get; set; }
}
