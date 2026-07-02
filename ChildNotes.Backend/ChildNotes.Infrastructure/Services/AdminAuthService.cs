using System.Security.Cryptography;
using System.Text;
using ChildNotes.Core.Common;
using ChildNotes.Core.Config;
using ChildNotes.Core.Constants;
using ChildNotes.Core.Dtos;
using ChildNotes.Core.Entities;
using ChildNotes.Core.Exceptions;
using ChildNotes.Core.Services;
using ChildNotes.Infrastructure.Auth;
using ChildNotes.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChildNotes.Infrastructure.Services;

public class AdminAuthService : IAdminAuthService
{
    private static readonly object _initLock = new();
    private readonly ChildNotesDbContext _db;
    private readonly AdminOptions _opt;
    private readonly ICurrentAdminService _current;
    private readonly IPasswordHasher _passwordHasher;

    public AdminAuthService(
        ChildNotesDbContext db,
        IOptions<AdminOptions> opt,
        ICurrentAdminService current,
        IPasswordHasher passwordHasher)
    {
        _db = db;
        _opt = opt.Value;
        _current = current;
        _passwordHasher = passwordHasher;
    }

    public async Task EnsureDefaultAdminAsync(CancellationToken ct = default)
    {
        if (await _db.AdminAccounts.AnyAsync(ct)) return;
        if (string.IsNullOrEmpty(_opt.InitPassword)) return;

        lock (_initLock)
        {
            if (_db.AdminAccounts.Any()) return;
            _db.AdminAccounts.Add(new AdminAccount
            {
                Id = Guid.NewGuid().ToString("N"),
                Username = _opt.InitUsername,
                PasswordHash = _passwordHasher.Hash(_opt.InitPassword),
                DisplayName = _opt.InitDisplayName,
                Status = StatusConstants.Admin.Active,
            });
            _db.SaveChanges();
        }
    }

    public async Task<AdminLoginResponse> LoginAsync(AdminLoginRequest req, CancellationToken ct = default)
    {
        await EnsureDefaultAdminAsync(ct);

        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            throw new BusinessException("Invalid username or password", 400);

        var admin = await _db.AdminAccounts.FirstOrDefaultAsync(a => a.Username == req.Username, ct);
        if (admin is null || admin.Status != StatusConstants.Admin.Active
            || !_passwordHasher.Verify(req.Password, admin.PasswordHash))
            throw new BusinessException("Invalid username or password", 400);

        // 生成随机 token，但数据库只存其 SHA256 哈希，避免数据库泄露后 token 被直接复用
        var rawToken = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        admin.Token = HashToken(rawToken);
        admin.TokenExpireAt = DateTime.UtcNow.AddHours(_opt.TokenExpireHours);
        admin.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return new AdminLoginResponse
        {
            AdminId = admin.Id,
            Username = admin.Username,
            DisplayName = admin.DisplayName,
            Token = rawToken,
            TokenExpireAt = DateTimeFormatter.FormatDateTime(admin.TokenExpireAt!.Value),
        };
    }

    public async Task<AdminAccount?> AuthenticateAsync(string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(token)) return null;
        var tokenHash = HashToken(token);
        return await _db.AdminAccounts.FirstOrDefaultAsync(
            a => a.Token == tokenHash && a.Status == StatusConstants.Admin.Active && a.TokenExpireAt > DateTime.UtcNow, ct);
    }

    public Task<AdminAccount?> GetCurrentAdminAsync(CancellationToken ct = default)
        => Task.FromResult(_current.Admin);

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        var admin = _current.Admin;
        if (admin is null) return;
        admin.Token = null;
        admin.TokenExpireAt = null;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// 将明文 token 转为 SHA256 哈希（64 位 hex 字符串）。
    /// 数据库只存哈希值，即使数据库泄露攻击者也无法直接复用 token 登录。
    /// </summary>
    private static string HashToken(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
