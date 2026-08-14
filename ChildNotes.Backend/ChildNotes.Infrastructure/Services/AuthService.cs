using System.Security.Cryptography;
using ChildNotes.Core.Common;
using ChildNotes.Core.Config;
using ChildNotes.Core.Dtos;
using ChildNotes.Core.Entities;
using ChildNotes.Core.Exceptions;
using ChildNotes.Core.Services;
using ChildNotes.Infrastructure.Auth;
using ChildNotes.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChildNotes.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly ChildNotesDbContext _db;
    private readonly JwtTokenService _jwt;
    private readonly ICurrentUserService _current;
    private readonly IEmailSender _emailSender;
    private readonly EmailAuthOptions _opt;
    private readonly PointsWalletService _wallet;

    public AuthService(
        ChildNotesDbContext db,
        JwtTokenService jwt,
        ICurrentUserService current,
        IEmailSender emailSender,
        IOptions<EmailAuthOptions> opt,
        PointsWalletService wallet)
    {
        _db = db;
        _jwt = jwt;
        _current = current;
        _emailSender = emailSender;
        _opt = opt.Value;
        _wallet = wallet;
    }

    public async Task<SendCodeResponse> SendCodeAsync(SendCodeRequest req, CancellationToken ct = default)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(email))
            throw new BusinessException("邮箱不能为空", 400, "EMAIL_EMPTY");

        // 限流：同邮箱 60 秒内只能发一次
        var lastCode = await _db.EmailVerificationCodes
            .Where(c => c.Email == email && c.ConsumedAt == null)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (lastCode is not null && lastCode.CreatedAt > DateTime.UtcNow.AddSeconds(-_opt.ResendIntervalSeconds))
            throw new BusinessException($"验证码发送过于频繁，请{_opt.ResendIntervalSeconds}秒后重试", 429, "RATE_LIMITED");

        // 使同邮箱未消费的旧码失效
        if (lastCode is not null)
        {
            lastCode.ConsumedAt = DateTime.UtcNow;
        }

        // 生成验证码
        var code = RandomNumberGenerator.GetInt32(0, (int)Math.Pow(10, _opt.CodeLength))
            .ToString($"D{_opt.CodeLength}");

        var codeHash = _jwt.HashToken(code);
        var record = new EmailVerificationCode
        {
            Id = Guid.NewGuid().ToString("N"),
            Email = email,
            CodeHash = codeHash,
            ExpiresAt = DateTime.UtcNow.AddSeconds(_opt.CodeTtlSeconds),
            CreatedAt = DateTime.UtcNow,
        };
        _db.EmailVerificationCodes.Add(record);
        await _db.SaveChangesAsync(ct);

        // 发送邮件
        var subject = "ChildNotes 验证码";
        var htmlBody = $@"
<div style='font-family:sans-serif;max-width:400px;margin:0 auto;padding:20px;'>
  <h2 style='color:#e83e8c;'>ChildNotes</h2>
  <p>您的验证码是：</p>
  <div style='font-size:32px;font-weight:bold;letter-spacing:8px;color:#e83e8c;padding:16px 0;text-align:center;'>
    {code}
  </div>
  <p style='color:#999;font-size:12px;'>验证码 {_opt.CodeTtlSeconds / 60} 分钟内有效，请勿泄露给他人。</p>
</div>";
        await _emailSender.SendAsync(email, subject, htmlBody, ct);

        return new SendCodeResponse { Sent = true };
    }

    public async Task<AuthResponse> VerifyCodeAsync(VerifyCodeRequest req, CancellationToken ct = default)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(req.Code))
            throw new BusinessException("邮箱和验证码不能为空", 400, "INVALID_INPUT");

        // 查未消费的验证码
        var record = await _db.EmailVerificationCodes
            .Where(c => c.Email == email && c.ConsumedAt == null)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct)
            ?? throw new BusinessException("验证码不存在或已使用，请重新发送", 400, "CODE_NOT_FOUND");

        // 检查过期
        if (record.ExpiresAt < DateTime.UtcNow)
            throw new BusinessException("验证码已过期，请重新发送", 400, "CODE_EXPIRED");

        // 检查尝试次数
        if (record.AttemptCount >= _opt.MaxAttempts)
            throw new BusinessException("验证码错误次数过多，请重新发送", 400, "CODE_MAX_ATTEMPTS");

        // 校验验证码
        if (!_jwt.VerifyToken(req.Code, record.CodeHash))
        {
            record.AttemptCount++;
            await _db.SaveChangesAsync(ct);
            throw new BusinessException($"验证码错误，剩余尝试次数 {_opt.MaxAttempts - record.AttemptCount} 次", 400, "CODE_WRONG");
        }

        // 验证成功，消费验证码
        record.ConsumedAt = DateTime.UtcNow;

        // 查找或创建用户
        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Email == email, ct);
        var newUser = false;
        if (user is null)
        {
            user = new AppUser
            {
                Id = Guid.NewGuid().ToString("N"),
                Email = email,
                EmailVerifiedAt = DateTime.UtcNow,
                NickName = "用户" + email.Split('@')[0],
            };
            _db.AppUsers.Add(user);
            newUser = true;
        }
        else if (user.EmailVerifiedAt is null)
        {
            user.EmailVerifiedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        if (newUser)
        {
            await EnsureUserPointsAsync(user.Id, ct);
        }

        return await BuildAuthResponseAsync(user, newUser, ct);
    }

    public async Task<AuthResponse> RefreshAsync(RefreshRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(req.RefreshToken))
            throw new BusinessException("RefreshToken 不能为空", 400, "INVALID_INPUT");

        // 计算提交 token 的 hash
        // 由于 PBKDF2 每次 hash 带 random salt，不能直接 hash 后查数据库
        // 需要遍历未撤销的 token 逐一验证
        var activeTokens = await _db.RefreshTokens
            .Where(t => t.RevokedAt == null && t.ExpiresAt > DateTime.UtcNow)
            .ToListAsync(ct);

        RefreshToken? matchedToken = null;
        foreach (var t in activeTokens)
        {
            if (_jwt.VerifyToken(req.RefreshToken, t.TokenHash))
            {
                matchedToken = t;
                break;
            }
        }

        if (matchedToken is null)
            throw new BusinessException("RefreshToken 无效或已过期", 401, "REFRESH_TOKEN_INVALID");

        // Rotation：撤销旧 token
        matchedToken.RevokedAt = DateTime.UtcNow;

        // 查用户
        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Id == matchedToken.UserId, ct)
            ?? throw new UnauthorizedException();

        await _db.SaveChangesAsync(ct);

        return await BuildAuthResponseAsync(user, false, ct);
    }

    public async Task<LoginUserDto> GetCurrentUserAsync(CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Id == uid, ct)
            ?? throw new UnauthorizedException();
        return ToLoginUserDto(user);
    }

    public async Task<LoginUserDto> UpdateProfileAsync(UpdateProfileRequest req, CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Id == uid, ct)
            ?? throw new UnauthorizedException();
        if (req.NickName is not null) user.NickName = req.NickName;
        if (req.AvatarUrl is not null) user.AvatarUrl = req.AvatarUrl;
        if (req.Gender is not null) user.Gender = req.Gender.Value;
        await _db.SaveChangesAsync(ct);
        return ToLoginUserDto(user);
    }

    private async Task<AuthResponse> BuildAuthResponseAsync(AppUser user, bool newUser, CancellationToken ct)
    {
        var (accessToken, accessExpireAt) = _jwt.CreateAccessToken(user);
        var (refreshTokenRaw, refreshExpireAt) = _jwt.CreateRefreshToken(out var refreshHash);

        // 存储 refreshToken hash
        _db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = user.Id,
            TokenHash = refreshHash,
            ExpiresAt = refreshExpireAt,
            CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(ct);

        return new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshTokenRaw,
            ExpiresIn = _opt.AccessTokenExpireMinutes * 60,
            User = ToLoginUserDto(user),
            NewUser = newUser,
        };
    }

    private async Task EnsureUserPointsAsync(string userId, CancellationToken ct)
    {
        var p = await _db.UserPoints.FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (p is not null) return;
        p = new UserPoints { Id = Guid.NewGuid().ToString("N"), UserId = userId };
        _db.UserPoints.Add(p);
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            await _db.UserPoints.FirstOrDefaultAsync(x => x.UserId == userId, ct);
            return;
        }
        try { await _wallet.ChangeAsync(userId, PointsConstants.NewUserBonusPoints, ct); }
        catch (BusinessException) { }
    }

    private static LoginUserDto ToLoginUserDto(AppUser u) => new()
    {
        Id = u.Id,
        Email = u.Email,
        NickName = u.NickName,
        AvatarUrl = u.AvatarUrl,
        Gender = u.Gender,
        MembershipExpireAt = u.MembershipExpireAt?.ToString("O"),
        IsMember = Shared.Constants.MembershipConstants.IsActive(u.MembershipExpireAt),
    };
}
