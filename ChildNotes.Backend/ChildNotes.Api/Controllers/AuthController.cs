using ChildNotes.Core.Dtos;
using ChildNotes.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChildNotes.Api.Controllers;

[Route("api/auth")]
public class AuthController : AppBaseController
{
    private readonly IAuthService _auth;
    public AuthController(IAuthService auth) => _auth = auth;

    [AllowAnonymous]
    [HttpPost("send-code")]
    public async Task<SendCodeResponse> SendCode([FromBody] SendCodeRequest req, CancellationToken ct)
        => await _auth.SendCodeAsync(req, ct);

    [AllowAnonymous]
    [HttpPost("verify-code")]
    public async Task<AuthResponse> VerifyCode([FromBody] VerifyCodeRequest req, CancellationToken ct)
        => await _auth.VerifyCodeAsync(req, ct);

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<AuthResponse> Refresh([FromBody] RefreshRequest req, CancellationToken ct)
        => await _auth.RefreshAsync(req, ct);

    [HttpGet("me")]
    public async Task<LoginUserDto> Me(CancellationToken ct)
        => await _auth.GetCurrentUserAsync(ct);

    [HttpPut("profile")]
    public async Task<LoginUserDto> UpdateProfile([FromBody] UpdateProfileRequest req, CancellationToken ct)
        => await _auth.UpdateProfileAsync(req, ct);
}
