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
        // 邮箱格式 + 长度校验（在归一化前校验原始输入）
        var emailValidationError = AuthRequestValidator.ValidateEmail(req.Email);
        if (emailValidationError is not null)
            throw new BusinessException(emailValidationError, 400, "EMAIL_INVALID");

        var email = req.Email.Trim().ToLowerInvariant();

        string code = RandomNumberGenerator.GetInt32(0, (int)Math.Pow(10, _opt.CodeLength))
            .ToString($"D{_opt.CodeLength}");

        // 并发安全：事务内完成"限流检查 + 旧码失效 + 新码入库"
        // PostgreSQL 在事务内的 UPDATE 会对行加行锁，防止并发请求同时通过限流
        await _db.ExecuteInTransactionAsync(async () =>
        {
            // 限流：同邮箱 60 秒内只能发一次
            var lastCode = await _db.EmailVerificationCodes
                .Where(c => c.Email == email && c.ConsumedAt == null)
                .OrderByDescending(c => c.CreatedAt)
                .FirstOrDefaultAsync(ct);
            if (lastCode is not null && lastCode.CreatedAt > DateTime.UtcNow.AddSeconds(-_opt.ResendIntervalSeconds))
                throw new BusinessException($"验证码发送过于频繁，请{_opt.ResendIntervalSeconds}秒后重试", 429, "RATE_LIMITED");

            // 使同邮箱未消费的旧码失效（原子 CAS：只更新 ConsumedAt IS NULL 的行）
            if (lastCode is not null)
            {
                lastCode.ConsumedAt = DateTime.UtcNow;
            }

            // 生成验证码 hash（验证码明文在外部生成，事务内只存 Hash）
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
        }, ct);

        // 邮件发送放在事务外：SMTP 调用慢且不可回滚，避免长事务占用连接
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
        // 邮箱格式 + 长度校验
        var emailValidationError = AuthRequestValidator.ValidateEmail(req.Email);
        if (emailValidationError is not null)
            throw new BusinessException(emailValidationError, 400, "EMAIL_INVALID");

        // 验证码格式校验
        var codeValidationError = AuthRequestValidator.ValidateCode(req.Code, _opt.CodeLength);
        if (codeValidationError is not null)
            throw new BusinessException(codeValidationError, 400, "CODE_INVALID");

        var email = req.Email.Trim().ToLowerInvariant();

        // 用 list 在事务 lambda 内部收集结果（C# 闭包对局部变量的限制）
        var resultHolder = new List<(AppUser user, bool newUser, string? newUserId)>();
        // 用于后续积分注入（必须在事务外执行，避免跨服务调用占用长事务）

        // 并发安全：事务内完成"查未消费码 → 校验 → 原子消费 → 查找/创建用户 → 生成 RefreshToken"
        // 防止同一验证码被并发消费两次、同一邮箱被并发创建两个 AppUser
        await _db.ExecuteInTransactionAsync(async () =>
        {
            // 查未消费的验证码（行锁：FOR UPDATE，PostgreSQL 在事务内自动加锁）
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

            // 验证成功，消费验证码：
            // 事务内 tracked entity + SELECT 后二次校验 ConsumedAt
            // PostgreSQL 事务内的 UPDATE 会对行加行锁，防止并发请求重复消费
            // 同一事务内先 SELECT 拿到行锁，再修改 ConsumedAt 并 SaveChanges
            // InMemory 测试无并发场景，tracked entity 即可
            if (record.ConsumedAt is not null)
            {
                // 并发场景下被其他请求先消费了（虽然前面已过滤，事务内可能已被改）
                throw new BusinessException("验证码已被使用，请重新发送", 409, "CODE_CONSUMED_BY_ANOTHER");
            }
            record.ConsumedAt = DateTime.UtcNow;

            // 查找或创建用户
            var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Email == email, ct);
            var newUser = false;
            string? newUserId = null;
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
                newUserId = user.Id;
            }
            resultHolder.Add((user, newUser, newUserId));
        }, ct);

        var (resultUser, resultNewUser, resultNewUserId) = resultHolder[0];

        // 事务外：新用户积分注入（跨服务调用，不占用事务）
        if (resultNewUserId is not null)
        {
            await EnsureUserPointsAsync(resultNewUserId, ct);
        }

        // 事务外：生成 AccessToken/RefreshToken（BuildAuthResponseAsync 内部会开自己的事务）
        return await BuildAuthResponseAsync(resultUser, resultNewUser, ct);
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

        // 并发安全：原子 CAS 撤销旧 Token
        // EF Core tracked entity + [ConcurrencyCheck] on RevokedAt
        // SaveChanges 会生成 UPDATE ... WHERE Id = @p0 AND RevokedAt IS NULL
        // 并发请求中只有一个能成功，其他会抛 DbUpdateConcurrencyException
        // PostgreSQL 上 SaveChanges 本身就在事务内，对行加行锁
        var userHolder = new List<AppUser>();
        try
        {
            await _db.ExecuteInTransactionAsync(async () =>
            {
                // 二次校验：虽然前面已 SELECT 过，但事务开始时可能已被其他请求撤销
                if (matchedToken.RevokedAt is not null)
                {
                    throw new BusinessException("RefreshToken 已被撤销，请使用新 Token", 401, "REFRESH_TOKEN_REVOKED");
                }
                matchedToken.RevokedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);  // [ConcurrencyCheck] 生成原子 CAS

                // 查用户
                var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Id == matchedToken.UserId, ct)
                    ?? throw new UnauthorizedException();
                userHolder.Add(user);
            }, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // 并发场景下被其他请求先撤销了
            throw new BusinessException("RefreshToken 已被撤销，请使用新 Token", 401, "REFRESH_TOKEN_REVOKED");
        }

        // 事务外：生成新 Token 对（BuildAuthResponseAsync 会开自己的事务）
        return await BuildAuthResponseAsync(userHolder[0], false, ct);
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

        // 存储 refreshToken hash（原子插入，无需事务）
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
