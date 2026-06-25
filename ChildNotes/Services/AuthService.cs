using System.Security.Cryptography;
using System.Text;
using ChildNotes.Data.Repositories;
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
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return LoginResult.Fail("用户名和密码不能为空");
        if (username.Length < 3)
            return LoginResult.Fail("用户名至少 3 个字符");
        if (password.Length < 6)
            return LoginResult.Fail("密码至少 6 个字符");
        if (_users.FindByUsername(username) is not null)
            return LoginResult.Fail("用户名已存在");

        var user = new AppUser
        {
            Username = username,
            PasswordHash = HashPassword(password),
            NickName = string.IsNullOrWhiteSpace(nickName) ? username : nickName,
        };
        user.Id = _users.Insert(user);
        CurrentUser = user;
        return LoginResult.Ok(user);
    }

    public LoginResult Login(string username, string password)
    {
        var user = _users.FindByUsername(username);
        if (user is null)
            return LoginResult.Fail("用户不存在");
        if (!VerifyPassword(password, user.PasswordHash))
            return LoginResult.Fail("密码错误");
        CurrentUser = user;
        return LoginResult.Ok(user);
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
