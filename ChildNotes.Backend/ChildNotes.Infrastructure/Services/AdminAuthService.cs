using System.Collections.Concurrent;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChildNotes.Infrastructure.Services;

public class AdminAuthService : IAdminAuthService
{
    // 进程内初始化信号量：替代 lock（lock 内无法 await）；并发初始化只有一个请求执行建号
    private static readonly SemaphoreSlim _initLock = new(1, 1);

    // 登录失败锁定：账号级内存计数，5 次失败锁 15 分钟（进程级，重启清零——限流中间件是第二道防线）
    private static readonly ConcurrentDictionary<string, (int FailCount, DateTime LockedUntil)> _loginFailures = new();
    private const int MaxLoginFailures = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly ChildNotesDbContext _db;
    private readonly AdminOptions _opt;
    private readonly ICurrentAdminService _current;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<AdminAuthService> _logger;

    public AdminAuthService(
        ChildNotesDbContext db,
        IOptions<AdminOptions> opt,
        ICurrentAdminService current,
        IPasswordHasher passwordHasher,
        ILogger<AdminAuthService> logger)
    {
        _db = db;
        _opt = opt.Value;
        _current = current;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task EnsureDefaultAdminAsync(CancellationToken ct = default)
    {
        if (await _db.AdminAccounts.AnyAsync(ct)) return;
        if (string.IsNullOrEmpty(_opt.InitPassword)) return;

        await _initLock.WaitAsync(ct);
        try
        {
            // 双重检查：等待期间可能已被并发请求创建（username 唯一索引兜底）
            if (await _db.AdminAccounts.AnyAsync(ct)) return;
            _db.AdminAccounts.Add(new AdminAccount
            {
                Id = Guid.NewGuid().ToString("N"),
                Username = _opt.InitUsername,
                PasswordHash = _passwordHasher.Hash(_opt.InitPassword),
                DisplayName = _opt.InitDisplayName,
                Status = StatusConstants.Admin.Active,
            });
            await _db.SaveChangesAsync(ct);
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<AdminLoginResponse> LoginAsync(AdminLoginRequest req, CancellationToken ct = default)
    {
        await EnsureDefaultAdminAsync(ct);

        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            throw new BusinessException("Invalid username or password", 400);

        // 账号锁定检查：连续失败达阈值后直接拒绝，防在线爆破
        var now = DateTime.UtcNow;
        if (_loginFailures.TryGetValue(req.Username, out var failure))
        {
            if (failure.LockedUntil > now)
                throw new BusinessException($"账号已被临时锁定，请于 {(failure.LockedUntil - now).Minutes + 1} 分钟后重试", 429);
            if (failure.LockedUntil != default && failure.LockedUntil <= now)
                _loginFailures.TryRemove(req.Username, out _); // 锁定期已过，清除计数
        }

        var admin = await _db.AdminAccounts.FirstOrDefaultAsync(a => a.Username == req.Username, ct);
        if (admin is null || admin.Status != StatusConstants.Admin.Active
            || !_passwordHasher.Verify(req.Password, admin.PasswordHash))
        {
            // 审计日志：登录失败（用户名不存在/密码错/状态非 Active），便于追溯爆破尝试
            _logger.LogWarning("Admin 登录失败: username={Username}", req.Username);
            RecordLoginFailure(req.Username, now);
            throw new BusinessException("Invalid username or password", 400);
        }

        // 登录成功：清除失败计数
        _loginFailures.TryRemove(req.Username, out _);

        // 自动迁移历史明文密码到 PBKDF2 格式
        if (_passwordHasher.NeedsUpgrade(admin.PasswordHash))
        {
            admin.PasswordHash = _passwordHasher.Hash(req.Password);
        }

        // 生成随机 token，数据库只存 SHA-256 哈希（防拖库后直接复用）；
        // token 为 64 位 hex 高熵随机数，SHA-256 足够（与 RefreshToken.TokenHashFast 同方案）
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
        // 数据库存的是 SHA-256 哈希，按哈希索引查询（token 列有索引）
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

    private static void RecordLoginFailure(string username, DateTime now)
    {
        var updated = _loginFailures.AddOrUpdate(username,
            _ => (1, default),
            (_, f) => f.LockedUntil > now ? f : (f.FailCount + 1, f.LockedUntil));
        if (updated.FailCount >= MaxLoginFailures && updated.LockedUntil == default)
        {
            _loginFailures[username] = (updated.FailCount, now.Add(LockoutDuration));
        }
    }

    private static string HashToken(string raw)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
