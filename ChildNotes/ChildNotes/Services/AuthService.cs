using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChildNotes.Data.Repositories;
using ChildNotes.Infrastructure;
using ChildNotes.Models;

namespace ChildNotes.Services;

public sealed class AuthService
{
    private readonly UserRepository _users;
    private readonly SessionRepository _sessions;
    private readonly AppState _state;
    private readonly SyncConfigRepository _cfgRepo;

    /// <summary>会话有效期 30 天（滑动过期：每次启动自动续期）。</summary>
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);

    public AppUser? CurrentUser { get; private set; }

    public AuthService(UserRepository users, SessionRepository sessions, AppState state, SyncConfigRepository cfgRepo)
    {
        _users = users;
        _sessions = sessions;
        _state = state;
        _cfgRepo = cfgRepo;
    }

    public bool IsLoggedIn => CurrentUser is not null;

    public LoginResult Register(string username, string password, string nickName)
    {
        DevLogger.Log("Auth", $"Register start: user='{username}', nick='{nickName}'");
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return Fail("用户名和密码不能为空");
        if (username.Length < 3)
            return Fail("用户名至少 3 个字符");
        if (password.Length < 6)
            return Fail("密码至少 6 个字符");

        try
        {
            var existing = _users.FindByUsername(username);
            DevLogger.Log("Auth", $"FindByUsername returned: {(existing is null ? "null" : existing.Username + "(id=" + existing.Id + ")")}");
            if (existing is not null)
                return Fail("用户名已存在");
        }
        catch (Exception ex)
        {
            DevLogger.Log("Auth", ex);
            throw;
        }

        var user = new AppUser
        {
            Username = username,
            PasswordHash = HashPassword(password),
            NickName = string.IsNullOrWhiteSpace(nickName) ? username : nickName,
        };
        try
        {
            user.Id = _users.Insert(user);
            DevLogger.Log("Auth", $"Insert success: new id={user.Id}");
        }
        catch (Exception ex)
        {
            DevLogger.Log("Auth", ex);
            throw;
        }
        CurrentUser = user;
        SaveSession(user.Id);
        DevLogger.Log("Auth", $"Register success: user={username}, id={user.Id}");
        // 本地注册成功后，尝试同步到后端（失败不阻塞，同步时还会重试登录）
        _ = TryRegisterOnServerAsync(username, password, nickName);
        return LoginResult.Ok(user);
    }

    /// <summary>
    /// 尝试在远端服务器创建同名账号。失败不影响本地注册（fire-and-forget），
    /// 后续同步时 ApiSyncService 会再次尝试登录，用户感知不到。
    /// </summary>
    private async Task TryRegisterOnServerAsync(string username, string password, string? nickName)
    {
        try
        {
            var cfg = _cfgRepo.Get();
            var serverUrl = cfg.ServerUrl;
            if (string.IsNullOrWhiteSpace(serverUrl))
            {
                DevLogger.Log("Auth", "Skip remote register: server url not configured");
                return;
            }

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var body = JsonSerializer.Serialize(new
            {
                username,
                password,
                nickName = string.IsNullOrWhiteSpace(nickName) ? username : nickName,
            });
            using var resp = await http.PostAsync(
                serverUrl.TrimEnd('/') + "/api/auth/register",
                new StringContent(body, Encoding.UTF8, "application/json"));

            var text = await resp.Content.ReadAsStringAsync();
            if (resp.IsSuccessStatusCode)
                DevLogger.Log("Auth", $"Remote register ok: user={username}, status={resp.StatusCode}");
            else
                DevLogger.Log("Auth", $"Remote register non-2xx: status={resp.StatusCode}, body={text}");
        }
        catch (Exception ex)
        {
            // 常见原因：服务器未启动/地址未配置/网络不通。仅记日志，不抛出。
            DevLogger.Log("Auth", $"Remote register failed (non-fatal): {ex.Message}");
        }
    }

    public LoginResult Login(string username, string password)
    {
        DevLogger.Log("Auth", $"Login start: user='{username}'");
        AppUser? user;
        try
        {
            user = _users.FindByUsername(username);
        }
        catch (Exception ex)
        {
            DevLogger.Log("Auth", ex);
            throw;
        }

        if (user is null)
        {
            DevLogger.Log("Auth", "Login fail: user not found");
            return Fail("用户不存在");
        }
        DevLogger.Log("Auth", $"User found: id={user.Id}, hashLen={user.PasswordHash?.Length ?? 0}");

        bool ok;
        try
        {
            ok = VerifyPassword(password, user.PasswordHash);
        }
        catch (Exception ex)
        {
            DevLogger.Log("Auth", ex);
            throw;
        }
        if (!ok)
        {
            DevLogger.Log("Auth", "Login fail: password mismatch");
            return Fail("密码错误");
        }
        CurrentUser = user;
        SaveSession(user.Id);
        DevLogger.Log("Auth", $"Login success: user={username}, id={user.Id}");
        return LoginResult.Ok(user);
    }

    private static LoginResult Fail(string msg)
    {
        DevLogger.Log("Auth", "Fail: " + msg);
        return LoginResult.Fail(msg);
    }

    public void Logout()
    {
        _sessions.Clear();
        CurrentUser = null;
        _state.Clear();
    }

    public void UpdateProfile(string nickName, string avatarUrl, int gender)
    {
        if (CurrentUser is null) return;
        CurrentUser.NickName = nickName;
        CurrentUser.AvatarUrl = avatarUrl;
        CurrentUser.Gender = gender;
        _users.UpdateProfile(CurrentUser);
    }

    /// <summary>
    /// 启动时尝试从持久化会话恢复登录态。
    /// 成功条件：存在会话记录 + 用户存在 + 未过期。
    /// 成功则滑动续期 30 天；失败（无会话/用户不存在/已过期）则清除会话并返回 false。
    /// </summary>
    public bool TryRestoreSession()
    {
        var session = _sessions.Get();
        if (session is null)
        {
            DevLogger.Log("Auth", "RestoreSession: no session record");
            return false;
        }

        if (DateTime.UtcNow >= session.ExpireAt)
        {
            DevLogger.Log("Auth", $"RestoreSession: expired (expireAt={session.ExpireAt:O})");
            _sessions.Clear();
            return false;
        }

        var user = _users.FindById(session.UserId);
        if (user is null)
        {
            DevLogger.Log("Auth", $"RestoreSession: user not found (id={session.UserId})");
            _sessions.Clear();
            return false;
        }

        CurrentUser = user;
        SaveSession(user.Id); // 滑动续期
        DevLogger.Log("Auth", $"RestoreSession success: user={user.Username}, id={user.Id}, renewed expireAt={DateTime.UtcNow + SessionLifetime:O}");
        return true;
    }

    private void SaveSession(string userId)
    {
        var now = DateTime.UtcNow;
        _sessions.Save(userId, now, now + SessionLifetime);
    }

    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, 10000, HashAlgorithmName.SHA256, 32);
        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password, string stored)
    {
        var parts = stored.Split(':');
        if (parts.Length != 2) return false;
        var salt = Convert.FromBase64String(parts[0]);
        var expected = Convert.FromBase64String(parts[1]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, 10000, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}

public sealed class LoginResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public AppUser? User { get; init; }

    public static LoginResult Ok(AppUser u) => new() { Success = true, User = u };
    public static LoginResult Fail(string msg) => new() { Success = false, Message = msg };
}
