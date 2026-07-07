using ChildNotes.Data;
using ChildNotes.Data.Repositories;
using ChildNotes.Infrastructure;
using ChildNotes.Services;
using SQLitePCL;

namespace ChildNotes.Tests;

/// <summary>
/// 验证登录会话持久化与自动登录功能。
/// 覆盖场景：正常登录→重启恢复、过期会话、用户不存在、退出后无法恢复、滑动续期。
/// </summary>
public class SessionPersistenceTests : IDisposable
{
    private readonly DbConnectionFactory _factory;
    private readonly UserRepository _userRepo;
    private readonly SessionRepository _sessionRepo;
    private readonly AppState _state;
    private readonly AuthService _auth;

    public SessionPersistenceTests()
    {
        // 初始化 SQLitePCL raw provider（生产代码在 App.axaml.cs 中调用 Batteries_V2.Init）
        Batteries_V2.Init();

        // 每个测试用例使用独立的临时 SQLite 文件
        var tmpDb = Path.Combine(Path.GetTempPath(), $"cn_test_{Guid.NewGuid():N}.db");
        try
        {
            _factory = new DbConnectionFactory(tmpDb);
            DbInitializer.Initialize(_factory);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"DbInitializer failed. tmpDb={tmpDb}, ex={ex.GetType().Name}: {ex.Message}", ex);
        }

        _userRepo = new UserRepository(_factory);
        _sessionRepo = new SessionRepository(_factory);
        _state = new AppState();
        _auth = new AuthService(_userRepo, _sessionRepo, _state, new SyncConfigRepository(_factory));
    }

    public void Dispose()
    {
        // DbConnectionFactory 仅持有连接字符串与 dbPath，不缓存 SqliteConnection 实例，
        // 每次 Create() 由调用方负责 using 释放，因此此处无需显式释放。
        // 临时 db 文件由 OS 在 temp 目录清理。
    }

    /// <summary>辅助：注册一个用户并退出（仅写入 app_user 表，不保留会话）。</summary>
    private void RegisterUser(string username, string password = "pass123")
    {
        var reg = _auth.Register(username, password, username);
        Assert.True(reg.Success, $"注册 {username} 应成功，实际：{reg.Message}");
        _auth.Logout(); // 清除注册时自动写入的会话，便于后续测试 Login 流程
    }

    [Fact]
    public void Login_PersistsSession_And_CanRestoreAfterRestart()
    {
        RegisterUser("testuser");

        // 模拟首次登录
        var loginResult = _auth.Login("testuser", "pass123");
        Assert.True(loginResult.Success, "登录应成功");
        Assert.NotNull(_auth.CurrentUser);

        var userId = _auth.CurrentUser!.Id;
        var firstExpireAt = _sessionRepo.Get()?.ExpireAt;
        Assert.NotNull(firstExpireAt);

        // 模拟应用关闭：重建 AuthService（CurrentUser 丢失）
        var newAuth = new AuthService(_userRepo, _sessionRepo, new AppState(), new SyncConfigRepository(_factory));

        // 恢复前未登录
        Assert.False(newAuth.IsLoggedIn);

        // 恢复会话
        var restored = newAuth.TryRestoreSession();

        // 验证恢复成功
        Assert.True(restored);
        Assert.True(newAuth.IsLoggedIn);
        Assert.Equal(userId, newAuth.CurrentUser!.Id);
        Assert.Equal("testuser", newAuth.CurrentUser!.Username);
    }

    [Fact]
    public void TryRestoreSession_NoSession_ReturnsFalse()
    {
        // 全新数据库，无会话记录
        var restored = _auth.TryRestoreSession();

        Assert.False(restored);
        Assert.False(_auth.IsLoggedIn);
        Assert.Null(_auth.CurrentUser);
    }

    [Fact]
    public void TryRestoreSession_ExpiredSession_ReturnsFalse_AndClearsSession()
    {
        RegisterUser("expiryuser");

        // 先登录写入会话
        var loginOk = _auth.Login("expiryuser", "pass123");
        Assert.True(loginOk.Success);
        var session = _sessionRepo.Get();
        Assert.NotNull(session);

        // 模拟应用重启：用新 AuthService 实例（CurrentUser 为空）
        var newAuth = new AuthService(_userRepo, _sessionRepo, new AppState(), new SyncConfigRepository(_factory));

        // 手动把过期时间改为过去
        var pastExpire = DateTime.UtcNow.AddMinutes(-1);
        _sessionRepo.Save(session!.UserId, session.IssuedAt, pastExpire);

        // 恢复应失败
        var restored = newAuth.TryRestoreSession();
        Assert.False(restored);
        Assert.False(newAuth.IsLoggedIn);

        // 过期会话应被清除
        Assert.Null(_sessionRepo.Get());
    }

    [Fact]
    public void TryRestoreSession_UserDeleted_ReturnsFalse_AndClearsSession()
    {
        RegisterUser("ghostuser");

        var loginOk = _auth.Login("ghostuser", "pass123");
        Assert.True(loginOk.Success);
        var session = _sessionRepo.Get();
        Assert.NotNull(session);

        // 模拟用户被删除（直接物理删除 app_user 记录）
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM app_user WHERE id=@i";
        cmd.Parameters.AddWithValue("@i", session!.UserId);
        cmd.ExecuteNonQuery();

        // 模拟应用重启：用新 AuthService 实例
        var newAuth = new AuthService(_userRepo, _sessionRepo, new AppState(), new SyncConfigRepository(_factory));

        // 恢复应失败
        var restored = newAuth.TryRestoreSession();
        Assert.False(restored);
        Assert.False(newAuth.IsLoggedIn);

        // 会话应被清除
        Assert.Null(_sessionRepo.Get());
    }

    [Fact]
    public void Logout_ClearsSession_And_RestoreFails()
    {
        RegisterUser("logoutuser");

        var loginOk = _auth.Login("logoutuser", "pass123");
        Assert.True(loginOk.Success);
        Assert.True(_auth.IsLoggedIn);
        Assert.NotNull(_sessionRepo.Get());

        _auth.Logout();

        // 内存态清空
        Assert.False(_auth.IsLoggedIn);
        Assert.Null(_auth.CurrentUser);
        Assert.Null(_state.User);

        // 持久化会话也清空
        Assert.Null(_sessionRepo.Get());

        // 模拟重启后恢复应失败
        var newAuth = new AuthService(_userRepo, _sessionRepo, new AppState(), new SyncConfigRepository(_factory));
        Assert.False(newAuth.TryRestoreSession());
    }

    [Fact]
    public void TryRestoreSession_RenewsExpireAt_SlidingWindow()
    {
        RegisterUser("slidinguser");

        var loginOk = _auth.Login("slidinguser", "pass123");
        Assert.True(loginOk.Success);
        var originalExpire = _sessionRepo.Get()!.ExpireAt;

        // 等待一小段时间确保时间戳不同
        Thread.Sleep(50);

        // 模拟重启并恢复
        var newAuth = new AuthService(_userRepo, _sessionRepo, new AppState(), new SyncConfigRepository(_factory));
        var restored = newAuth.TryRestoreSession();

        Assert.True(restored);
        var renewedExpire = _sessionRepo.Get()!.ExpireAt;

        // 滑动续期：新的过期时间应晚于原过期时间
        Assert.True(renewedExpire > originalExpire, $"期望续期后 {renewedExpire:O} > 原 {originalExpire:O}");

        // 且仍为 30 天有效期
        var remaining = renewedExpire - DateTime.UtcNow;
        Assert.InRange(remaining.TotalDays, 29.9, 30.1);
    }

    [Fact]
    public void Register_AlsoPersistsSession()
    {
        var result = _auth.Register("newreguser", "pass123", "Newbie");
        Assert.True(result.Success);

        // 注册后应有会话
        var session = _sessionRepo.Get();
        Assert.NotNull(session);
        Assert.Equal(result.User!.Id, session!.UserId);

        // 模拟重启恢复
        var newAuth = new AuthService(_userRepo, _sessionRepo, new AppState(), new SyncConfigRepository(_factory));
        Assert.True(newAuth.TryRestoreSession());
        Assert.Equal("newreguser", newAuth.CurrentUser!.Username);
    }

    [Fact]
    public void Session_Lifetime_Is30Days()
    {
        // 验证配置的会话有效期
        Assert.Equal(TimeSpan.FromDays(30), AuthService.SessionLifetime);
    }

    [Fact]
    public void MultipleLogins_OverwriteSameSessionRow()
    {
        RegisterUser("multi1");
        RegisterUser("multi2");

        // 登录 user1
        var login1 = _auth.Login("multi1", "pass123");
        Assert.True(login1.Success);
        var uid1 = _auth.CurrentUser!.Id;

        // 退出
        _auth.Logout();
        Assert.Null(_sessionRepo.Get());

        // 登录 user2
        var login2 = _auth.Login("multi2", "pass123");
        Assert.True(login2.Success);
        var uid2 = _auth.CurrentUser!.Id;

        // 会话表应只有一行，且指向 user2
        var session = _sessionRepo.Get();
        Assert.NotNull(session);
        Assert.Equal(uid2, session!.UserId);
        Assert.NotEqual(uid1, session.UserId);
    }
}
