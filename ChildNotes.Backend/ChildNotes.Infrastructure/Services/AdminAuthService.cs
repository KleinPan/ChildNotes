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

        // 自动迁移历史明文密码到 PBKDF2 格式
        if (_passwordHasher.NeedsUpgrade(admin.PasswordHash))
        {
            admin.PasswordHash = _passwordHasher.Hash(req.Password);
        }

        // 生成随机 token 并明文存入数据库，便于开发/调试期间直接查看当前有效 token
        // 注意：明文存储 token 在数据库泄露后可被直接复用，仅适用于开发阶段
        var rawToken = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        admin.Token = rawToken;
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
        return await _db.AdminAccounts.FirstOrDefaultAsync(
            a => a.Token == token && a.Status == StatusConstants.Admin.Active && a.TokenExpireAt > DateTime.UtcNow, ct);
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
}
