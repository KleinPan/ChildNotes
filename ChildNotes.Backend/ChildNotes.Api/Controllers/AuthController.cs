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
    [HttpPost("register")]
    public async Task<LoginResponse> Register([FromBody] RegisterRequest req, CancellationToken ct)
        => await _auth.RegisterAsync(req, ct);

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<LoginResponse> Login([FromBody] LoginRequest req, CancellationToken ct)
        => await _auth.LoginAsync(req, ct);

    [HttpGet("me")]
    public async Task<LoginUserDto> Me(CancellationToken ct)
        => await _auth.GetCurrentUserAsync(ct);

    [HttpPut("profile")]
    public async Task<LoginUserDto> UpdateProfile([FromBody] UpdateProfileRequest req, CancellationToken ct)
        => await _auth.UpdateProfileAsync(req, ct);
}
