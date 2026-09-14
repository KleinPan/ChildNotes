using System.Security.Cryptography;
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

public class AuthService : IAuthService
{
    private readonly ChildNotesDbContext _db;
    private readonly JwtTokenService _jwt;
    private readonly ICurrentUserService _current;
    private readonly IEmailSender _emailSender;
    private readonly EmailAuthOptions _opt;
    private readonly PointsWalletService _wallet;
    private readonly IFamilyService _familyService;

    public AuthService(
        ChildNotesDbContext db,
        JwtTokenService jwt,
        ICurrentUserService current,
        IEmailSender emailSender,
        IOptions<EmailAuthOptions> opt,
        PointsWalletService wallet,
        IFamilyService familyService)
    {
        _db = db;
        _jwt = jwt;
        _current = current;
        _emailSender = emailSender;
        _opt = opt.Value;
        _wallet = wallet;
        _familyService = familyService;
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

        // 并发安全：事务内完成"行级锁 + 限流检查 + 旧码失效 + 新码入库"
        // 关键修复：原方案首次发送时 SELECT 到 NULL，无行可锁，两个并发事务都能通过限流
        //   修复方案：PostgreSQL 用事务级 advisory lock（按 email hash），串行化同邮箱请求
        //   InMemory 测试无并发，跳过 advisory lock 直接走原逻辑
        await _db.ExecuteInTransactionAsync(async () =>
        {
            // PostgreSQL 事务级 advisory lock：同邮箱的并发请求在此排队
            // 事务提交/回滚时锁自动释放，无需手动 unlock
            // 锁 key 用 email 的稳定 hash（C# 端算，避免 SQL 注入）
            if (_db.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory")
            {
                var key = StableEmailHashKey(email);
                // pg_advisory_xact_lock(bigint)：负数 key 避免与系统保留段冲突
                // 注意：必须用显式 object[] + ct 命名传参，否则编译器会把 (long, CancellationToken) 匹配到
                // ExecuteSqlRawAsync(string, params object[]) 重载，把 ct 当成 SQL 参数传给 provider
                // 导致 InvalidOperationException: store type mapping for 'CancellationToken' not found
                await _db.Database.ExecuteSqlRawAsync(
                    "SELECT pg_advisory_xact_lock({0})", new object[] { -key }, ct);
            }

            // 限流：同邮箱 ResendIntervalSeconds 内只能发一次
            // 此时同邮箱的并发请求已被 advisory lock 串行化，可安全 SELECT
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

    /// <summary>
    /// 计算 email 的稳定 64 位 hash（用于 PostgreSQL advisory lock key）。
    /// 用 XOR 折叠 SHA256 字节，避免不同邮箱碰撞到同一 key。
    /// </summary>
    private static long StableEmailHashKey(string email)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(email));
        // 取前 8 字节作为 long，再 XOR 后 8 字节，降低碰撞概率
        long lo = BitConverter.ToInt64(bytes, 0);
        long hi = BitConverter.ToInt64(bytes, 8);
        return lo ^ hi;
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

        // 并发安全：事务内完成"行锁 → 查未消费码 → 校验 → 原子消费 → 查找/创建用户 → 生成 RefreshToken"
        // 防止同一验证码被并发消费两次、同一邮箱被并发创建两个 AppUser
        try
        {
            await _db.ExecuteInTransactionAsync(async () =>
            {
                // PostgreSQL 事务级 advisory lock：同邮箱的并发请求在此排队
                // 防止 SELECT 拿不到行锁的并发请求同时进入校验逻辑
                if (_db.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory")
                {
                    var key = StableEmailHashKey(email);
                    // 同上：显式 object[] + ct 命名传参，避免歧义重载（详见 SendCodeAsync 注释）
                    await _db.Database.ExecuteSqlRawAsync(
                        "SELECT pg_advisory_xact_lock({0})", new object[] { -key }, ct);
                }

                // 查未消费的验证码（advisory lock 已串行化同邮箱请求，可安全 SELECT）
                var record = await _db.EmailVerificationCodes
                    .Where(c => c.Email == email && c.ConsumedAt == null)
                    .OrderByDescending(c => c.CreatedAt)
                    .FirstOrDefaultAsync(ct)
                    ?? throw new BusinessException("验证码不存在或已使用，请重新发送", 400, "CODE_NOT_FOUND");

                // 检查过期
                if (record.ExpiresAt < DateTime.UtcNow)
                    throw new BusinessException("验证码已过期，请重新发送", 400, "CODE_EXPIRED");

                // 检查尝试次数（防御性，原子自增会再校验）
                if (record.AttemptCount >= _opt.MaxAttempts)
                    throw new BusinessException("验证码错误次数过多，请重新发送", 400, "CODE_MAX_ATTEMPTS");

                // 校验验证码
                if (!_jwt.VerifyToken(req.Code, record.CodeHash))
                {
                    // 原子自增 AttemptCount：relational provider 走 ExecuteUpdateAsync
                    // 生成 UPDATE ... SET attempt_count = attempt_count + 1
                    //   WHERE id = @p0 AND consumed_at IS NULL AND attempt_count < @max
                    // 并发场景下多个错误请求不会丢失更新（DB 层原子）
                    // InMemory 不支持 ExecuteUpdateAsync，降级为 tracked entity（测试无并发）
                    if (_db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
                    {
                        record.AttemptCount++;
                        await _db.SaveChangesAsync(ct);
                    }
                    else
                    {
                        var updated = await _db.EmailVerificationCodes
                            .Where(c => c.Id == record.Id
                                && c.ConsumedAt == null
                                && c.AttemptCount < _opt.MaxAttempts)
                            .ExecuteUpdateAsync(s => s
                                .SetProperty(c => c.AttemptCount, c => c.AttemptCount + 1), ct);
                        if (updated == 0)
                        {
                            // 已达上限或被并发消费
                            throw new BusinessException("验证码错误次数过多，请重新发送", 400, "CODE_MAX_ATTEMPTS");
                        }
                        // 同步 tracked entity 状态（避免后续 if 命中时状态不一致）
                        record.AttemptCount++;
                    }
                    throw new BusinessException($"验证码错误，剩余尝试次数 {_opt.MaxAttempts - record.AttemptCount} 次", 400, "CODE_WRONG");
                }

                // 验证成功，消费验证码：
                // [ConcurrencyCheck] on ConsumedAt 生成 UPDATE ... WHERE ConsumedAt IS NULL
                // 并发请求中只有一个能成功，其他抛 DbUpdateConcurrencyException
                if (record.ConsumedAt is not null)
                {
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

                await _db.SaveChangesAsync(ct);  // [ConcurrencyCheck] 生成原子 CAS

                if (newUser)
                {
                    newUserId = user.Id;
                }
                resultHolder.Add((user, newUser, newUserId));
            }, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // 并发场景下被其他请求先消费了验证码
            // 不返回 500，转换成明确的业务异常
            throw new BusinessException("验证码已被使用，请重新发送", 409, "CODE_CONSUMED_BY_ANOTHER");
        }

        var (resultUser, resultNewUser, resultNewUserId) = resultHolder[0];

        // 事务外：新用户积分注入（跨服务调用，不占用事务）
        if (resultNewUserId is not null)
        {
            await EnsureUserPointsAsync(resultNewUserId, ct);
            // 新用户自动建默认 Family（Owner）——Family-now 模型，见 docs/development/family-identity-architecture.md
            await EnsureDefaultFamilyAsync(resultNewUserId, ct);
        }

        // 事务外：生成 AccessToken/RefreshToken（BuildAuthResponseAsync 内部会开自己的事务）
        return await BuildAuthResponseAsync(resultUser, resultNewUser, ct);
    }

    public async Task<AuthResponse> RefreshAsync(RefreshRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(req.RefreshToken))
            throw new BusinessException("RefreshToken 不能为空", 400, "INVALID_INPUT");

        // Token 查找分两级：
        // 1) 快路径：SHA-256(token) 索引 O(1) 查找（新签发 token 均写入 token_hash_fast）
        // 2) 慢路径回退：快哈希 miss 时（2026-09 前签发的旧 token 无 fast 列），
        //    遍历候选 token 逐条 PBKDF2 验证。旧 token 30 天有效期自然过期后可移除。
        //
        // 候选范围 = 未过期且（未撤销 或 撤销时间在宽限期内）：
        // 宽限期（Grace Period）覆盖 Rotation 丢失场景——客户端因网络超时/并发重试/进程中断
        // 未保存新 token 时，旧 token 已被撤销，重放应换取新 token 而非 401（401 会导致客户端软登出）。
        var now = DateTime.UtcNow;
        var graceCutoff = now.AddSeconds(-_opt.RefreshGracePeriodSeconds);
        var fastHash = JwtTokenService.FastHashToken(req.RefreshToken);

        // 快路径：未过期且（未撤销 或 宽限期内撤销）+ fast 哈希命中（索引 O(1)）
        RefreshToken? matchedToken = await _db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHashFast == fastHash
                && t.ExpiresAt > now && (t.RevokedAt == null || t.RevokedAt > graceCutoff), ct);

        // 慢路径回退：旧数据（token_hash_fast 为 null 的历史 token）逐条 PBKDF2 验证
        if (matchedToken is null)
        {
            var candidateTokens = await _db.RefreshTokens
                .Where(t => t.TokenHashFast == null
                    && t.ExpiresAt > now && (t.RevokedAt == null || t.RevokedAt > graceCutoff))
                .ToListAsync(ct);
            foreach (var t in candidateTokens)
            {
                if (_jwt.VerifyToken(req.RefreshToken, t.TokenHash))
                {
                    matchedToken = t;
                    break;
                }
            }
        }

        if (matchedToken is null)
        {
            // 前驱链恢复（宽限期外的隔夜兜底）：宽限期（120s）只覆盖秒级竞态，覆盖不了
            // "客户端进程在保存新 token 前被杀、数小时后才重试"的场景（实测案例：
            // Rotation 后 12 小时重放旧 token 仍 401 → 软登出掉线）。
            //
            // 判定依据：重放的已撤销 token 若存在"仍活跃的继任者"（Rotation 撤销旧
            // token 后 ~0.3s 签发的新 token），该继任者必然从未送达任何客户端
            //（孤儿 token——响应丢失了），重放方即持有旧 token 的原设备。
            // 撤销继任者并签发新 token 对，自愈而非 401 软登出。
            //
            // 一次性保证：恢复动作本身会撤销继任者，同一旧 token 二次重放时
            // 继任者已撤销（无活跃继任者）→ 走 401，防盗用重放。
            RefreshToken? predecessor = await _db.RefreshTokens
                .FirstOrDefaultAsync(t => t.TokenHashFast == fastHash
                    && t.ExpiresAt > now && t.RevokedAt != null && t.RevokedAt <= graceCutoff, ct);

            // 慢路径回退：旧格式前驱 token
            if (predecessor is null)
            {
                var revokedCandidates = await _db.RefreshTokens
                    .Where(t => t.TokenHashFast == null
                        && t.ExpiresAt > now && t.RevokedAt != null && t.RevokedAt <= graceCutoff)
                    .ToListAsync(ct);
                foreach (var t in revokedCandidates)
                {
                    if (_jwt.VerifyToken(req.RefreshToken, t.TokenHash))
                    {
                        predecessor = t;
                        break;
                    }
                }
            }

            if (predecessor is not null)
            {
                var revokedAt = predecessor.RevokedAt!.Value;
                // 继任者 = 撤销 predecessor 后 10s 内签发、同用户、仍活跃的 token
                //（Rotation 事务中先撤销后签发，实测间隔 ~0.3s；取最早创建者）
                var successor = await _db.RefreshTokens
                    .Where(s => s.UserId == predecessor.UserId && s.RevokedAt == null && s.ExpiresAt > now
                        && s.CreatedAt >= revokedAt && s.CreatedAt <= revokedAt.AddSeconds(10))
                    .OrderBy(s => s.CreatedAt)
                    .FirstOrDefaultAsync(ct);

                if (successor is not null)
                {
                    try
                    {
                        await _db.ExecuteInTransactionAsync(async () =>
                        {
                            // 二次校验 + [ConcurrencyCheck] 原子 CAS：
                            // 并发恢复竞争时只有一方成功，败者按无效 token 处理
                            if (successor.RevokedAt is null)
                            {
                                successor.RevokedAt = DateTime.UtcNow;
                                await _db.SaveChangesAsync(ct);
                            }
                        }, ct);
                    }
                    catch (DbUpdateConcurrencyException)
                    {
                        throw new BusinessException("RefreshToken 无效或已过期", 401, "REFRESH_TOKEN_INVALID");
                    }

                    var recoveredUser = await _db.AppUsers
                        .FirstOrDefaultAsync(u => u.Id == predecessor.UserId, ct)
                        ?? throw new UnauthorizedException();
                    return await BuildAuthResponseAsync(recoveredUser, false, ct);
                }
            }

            throw new BusinessException("RefreshToken 无效或已过期", 401, "REFRESH_TOKEN_INVALID");
        }

        // 宽限期内已撤销的旧 token：视为合法重试（新 token 未送达客户端），直接再签发一对新 token。
        // 不再抛 401——那会让客户端清空登录态（软登出），造成"掉线"故障。
        if (matchedToken.RevokedAt is not null)
        {
            var graceUser = await _db.AppUsers.FirstOrDefaultAsync(u => u.Id == matchedToken.UserId, ct)
                ?? throw new UnauthorizedException();
            return await BuildAuthResponseAsync(graceUser, false, ct);
        }

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
                // 二次校验：虽然前面已 SELECT 过，但事务开始时可能已被其他请求撤销。
                // 该竞争必然发生在毫秒级窗口内（宽限期内），视为合法重试：
                // 跳过撤销，直接查用户走 rotation（与 DbUpdateConcurrencyException 分支同语义）。
                if (matchedToken.RevokedAt is null)
                {
                    matchedToken.RevokedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync(ct);  // [ConcurrencyCheck] 生成原子 CAS
                }

                // 查用户
                var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Id == matchedToken.UserId, ct)
                    ?? throw new UnauthorizedException();
                userHolder.Add(user);
            }, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // 并发请求携带同一 token 抢先撤销（毫秒级竞争，必然在宽限期内）：
            // 视为合法重试再签发新 token，避免 401 导致客户端误软登出掉线
            // （实测案例：两设备/进程 4 秒内先后 refresh 同一 token，后到者曾因此 401 掉线）。
            var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Id == matchedToken.UserId, ct)
                ?? throw new UnauthorizedException();
            userHolder.Add(user);
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
        var (refreshTokenRaw, refreshExpireAt) = _jwt.CreateRefreshToken(out var refreshHash, out var refreshFastHash);

        // 存储 refreshToken hash（原子插入，无需事务）
        _db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = user.Id,
            TokenHash = refreshHash,
            TokenHashFast = refreshFastHash,
            ExpiresAt = refreshExpireAt,
            CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(ct);

        var families = await _familyService.GetUserFamiliesAsync(user.Id, ct);
        return new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshTokenRaw,
            ExpiresIn = _opt.AccessTokenExpireMinutes * 60,
            User = ToLoginUserDto(user),
            NewUser = newUser,
            Families = families,
            // 与 FamilyService.GetUserFamiliesAsync 同序（CreatedAt 升序），MVP 取首个为当前家庭
            CurrentFamilyId = families.FirstOrDefault()?.Id,
        };
    }

    /// <summary>新用户建默认 Family（幂等：已是任意家庭成员则跳过）。</summary>
    private async Task EnsureDefaultFamilyAsync(string userId, CancellationToken ct)
    {
        var hasFamily = await _db.FamilyMembers.AnyAsync(fm => fm.UserId == userId, ct);
        if (hasFamily) return;
        var now = DateTime.UtcNow;
        var family = new Family { Id = Guid.NewGuid().ToString("N"), Name = "我的家庭", CreatedAt = now, UpdatedAt = now };
        _db.Families.Add(family);
        _db.FamilyMembers.Add(new FamilyMember
        {
            Id = Guid.NewGuid().ToString("N"),
            FamilyId = family.Id,
            UserId = userId,
            Role = StatusConstants.FamilyMemberRole.Owner,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await _db.SaveChangesAsync(ct);
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
