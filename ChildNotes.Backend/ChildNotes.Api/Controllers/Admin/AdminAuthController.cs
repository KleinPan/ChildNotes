using ChildNotes.Core.Dtos;
using ChildNotes.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace ChildNotes.Api.Controllers.Admin;

[ApiController]
[Route("admin/api/auth")]
public class AdminAuthController : AdminBaseController
{
    private readonly IAdminAuthService _auth;
    public AdminAuthController(IAdminAuthService auth) => _auth = auth;

    [HttpPost("login")]
    public async Task<AdminLoginResponse> Login([FromBody] AdminLoginRequest req, CancellationToken ct)
        => await _auth.LoginAsync(req, ct);

    [HttpGet("me")]
    public async Task<AdminLoginResponse> Me(CancellationToken ct)
    {
        var admin = await _auth.GetCurrentAdminAsync(ct)
            ?? throw new Core.Exceptions.UnauthorizedException("Admin login is required");
        return new AdminLoginResponse
        {
            AdminId = admin.Id,
            Username = admin.Username,
            DisplayName = admin.DisplayName,
            Token = admin.Token ?? "",
            TokenExpireAt = admin.TokenExpireAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "",
        };
    }

    [HttpPost("logout")]
    public async Task Logout(CancellationToken ct)
        => await _auth.LogoutAsync(ct);
}
