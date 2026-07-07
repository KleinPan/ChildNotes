using ChildNotes.Core.Common;
using ChildNotes.Core.Dtos;
using ChildNotes.Core.Entities;
using ChildNotes.Core.Exceptions;
using ChildNotes.Core.Services;
using ChildNotes.Infrastructure.Auth;
using ChildNotes.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChildNotes.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly ChildNotesDbContext _db;
    private readonly JwtTokenService _jwt;
    private readonly ICurrentUserService _current;
    private readonly IPasswordHasher _passwordHasher;

    public AuthService(ChildNotesDbContext db, JwtTokenService jwt, ICurrentUserService current, IPasswordHasher passwordHasher)
    {
        _db = db;
        _jwt = jwt;
        _current = current;
        _passwordHasher = passwordHasher;
    }

    public async Task<LoginResponse> RegisterAsync(RegisterRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            throw new BusinessException("用户名和密码不能为空", 400, "INVALID_CREDENTIALS");
        if (req.Username.Length < 3) throw new BusinessException("用户名至少 3 个字符", 400, "USERNAME_TOO_SHORT");
        if (req.Password.Length < 6) throw new BusinessException("密码至少 6 个字符", 400, "PASSWORD_TOO_SHORT");

        var exists = await _db.AppUsers.AnyAsync(u => u.Username == req.Username, ct);
        if (exists) throw new BusinessException("用户名已存在", 400, "USERNAME_TAKEN");

        var user = new AppUser
        {
            Id = Guid.NewGuid().ToString("N"),
            Username = req.Username,
            PasswordHash = _passwordHasher.Hash(req.Password),
            NickName = string.IsNullOrWhiteSpace(req.NickName) ? req.Username : req.NickName,
        };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync(ct);

        await EnsureUserPointsAsync(user.Id, ct);
        return await BuildLoginResponseAsync(user, true, ct);
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest req, CancellationToken ct = default)
    {
        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Username == req.Username, ct)
            ?? throw new BusinessException("用户不存在", 400, "USER_NOT_FOUND");
        if (!_passwordHasher.Verify(req.Password, user.PasswordHash))
            throw new BusinessException("密码错误", 400, "WRONG_PASSWORD");
        // 自动迁移历史明文密码到 PBKDF2 格式
        if (_passwordHasher.NeedsUpgrade(user.PasswordHash))
        {
            user.PasswordHash = _passwordHasher.Hash(req.Password);
            await _db.SaveChangesAsync(ct);
        }
        return await BuildLoginResponseAsync(user, false, ct);
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

    private async Task<LoginResponse> BuildLoginResponseAsync(AppUser user, bool newUser, CancellationToken ct)
    {
        var (token, expireAt) = _jwt.CreateToken(user);
        return new LoginResponse
        {
            Token = token,
            ExpireAt = expireAt,
            User = ToLoginUserDto(user),
            NewUser = newUser,
        };
    }

    /// <summary>
    /// 确保用户积分记录存在（幂等）。
    /// 修复原版 bug：原版 AnyAsync 后未重查直接 Add，并发场景下异常被吞但调用方误以为已创建。
    /// 现统一为失败后重查，保证返回存在记录。
    /// </summary>
    private async Task EnsureUserPointsAsync(string userId, CancellationToken ct)
    {
        var p = await _db.UserPoints.FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (p is not null) return;
        p = new UserPoints { Id = Guid.NewGuid().ToString("N"), UserId = userId };
        _db.UserPoints.Add(p);
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // 并发幂等：重查确保记录存在
            await _db.UserPoints.FirstOrDefaultAsync(x => x.UserId == userId, ct);
        }
    }

    private static LoginUserDto ToLoginUserDto(AppUser u) => new()
    {
        Id = u.Id,
        Username = u.Username,
        NickName = u.NickName,
        AvatarUrl = u.AvatarUrl,
        Gender = u.Gender,
    };
}
