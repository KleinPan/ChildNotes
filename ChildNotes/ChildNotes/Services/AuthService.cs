using System.Security.Cryptography;
using System.Text;
using ChildNotes.Data.Repositories;
using ChildNotes.Infrastructure;
using ChildNotes.Models;

namespace ChildNotes.Services;

public sealed class AuthService
{
    private readonly UserRepository _users;
    private readonly AppState _state;
    public AppUser? CurrentUser { get; private set; }

    public AuthService(UserRepository users, AppState state)
    {
        _users = users;
        _state = state;
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
        DevLogger.Log("Auth", $"Register success: user={username}, id={user.Id}");
        return LoginResult.Ok(user);
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

    public void RestoreSession(long userId)
    {
        CurrentUser = _users.FindById(userId);
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
