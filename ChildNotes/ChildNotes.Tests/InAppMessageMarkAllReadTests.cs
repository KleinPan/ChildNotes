using ChildNotes.Data;
using ChildNotes.Data.Repositories;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Services;
using SQLitePCL;

namespace ChildNotes.Tests;

/// <summary>
/// 诊断"全部已读"持久化问题：验证 MarkAllAsRead 后，重新查询消息是否真的为已读状态。
/// 复现路径：用户点"全部已读" → 返回 → 再次进入消息中心 → 重新 LoadAsync 从 DB 查询。
/// </summary>
public class InAppMessageMarkAllReadTests : IDisposable
{
    private readonly DbConnectionFactory _factory;
    private readonly InAppMessageRepository _repo;
    private readonly AppState _state;
    private readonly InAppMessageService _service;

    public InAppMessageMarkAllReadTests()
    {
        Batteries_V2.Init();
        var tmpDb = Path.Combine(Path.GetTempPath(), $"cn_msg_test_{Guid.NewGuid():N}.db");
        _factory = new DbConnectionFactory(tmpDb);
        DbInitializer.Initialize(_factory);
        _repo = new InAppMessageRepository(_factory);
        _state = new AppState();
        _state.User = new AppUser { Id = "user-1", Username = "u1", NickName = "U1" };
        _service = new InAppMessageService(_repo, _state);
    }

    [Fact]
    public void MarkAllAsRead_Should_Persist_To_Database()
    {
        // Arrange: 插入 3 条未读消息
        _service.Insert(new InAppMessage { UserId = "user-1", Title = "m1", Body = "b1", IsRead = false });
        _service.Insert(new InAppMessage { UserId = "user-1", Title = "m2", Body = "b2", IsRead = false });
        _service.Insert(new InAppMessage { UserId = "user-1", Title = "m3", Body = "b3", IsRead = false });

        // Act: 标记全部已读
        _service.MarkAllAsRead();

        // Assert: 直接从 DB 重新查询，应全部 is_read=1
        var reloaded = _repo.GetByUser("user-1");
        Assert.All(reloaded, m => Assert.True(m.IsRead, $"消息 {m.Title} 应为已读，实际 is_read={m.IsRead}"));
        Assert.Equal(3, reloaded.Count);
        Assert.Equal(0, _repo.CountUnread("user-1"));
    }

    [Fact]
    public void MarkAllAsRead_Then_Reload_Should_Still_Be_Read()
    {
        // 复现用户反馈场景：全部已读 → 返回 → 再次进入（重新 LoadAsync）
        _service.Insert(new InAppMessage { UserId = "user-1", Title = "welcome", Body = "hi", IsRead = false });
        _service.Insert(new InAppMessage { UserId = "user-1", Title = "op", Body = "op", IsRead = false });

        Assert.Equal(2, _service.GetUnreadCount());

        _service.MarkAllAsRead();
        Assert.Equal(0, _service.GetUnreadCount());

        // 模拟用户返回"我的"页后再次进入消息中心：重新调用 GetMessages
        var reloaded = _service.GetMessages();
        Assert.All(reloaded, m => Assert.True(m.IsRead, $"重新加载后消息 {m.Title} 应为已读"));
        Assert.Empty(reloaded.Where(m => !m.IsRead));
    }

    [Fact]
    public void MarkAllAsRead_With_No_User_Should_Be_NoOp()
    {
        // 边界：AppState.User 为 null 时不应抛异常，且不影响其他用户数据
        var emptyState = new AppState();
        var svc = new InAppMessageService(_repo, emptyState);
        _service.Insert(new InAppMessage { UserId = "user-1", Title = "m1", Body = "b1", IsRead = false });

        svc.MarkAllAsRead(); // User=null，应直接 return

        Assert.Equal(1, _repo.CountUnread("user-1")); // 不应被误标记为已读
    }

    [Fact]
    public void EnsureWelcomeMessage_Should_Be_Idempotent()
    {
        // 验证欢迎消息不会重复插入
        _service.EnsureWelcomeMessage();
        var first = _service.GetMessages();
        Assert.Single(first);

        _service.MarkAllAsRead();
        Assert.Equal(0, _service.GetUnreadCount());

        // 再次调用 EnsureWelcomeMessage：消息已存在（已读），不应重复插入
        _service.EnsureWelcomeMessage();
        var second = _service.GetMessages();
        Assert.Single(second); // 不应重复插入
        Assert.True(second[0].IsRead); // 已读状态应保留，不应被重置
    }

    [Fact]
    public void ClearReadMessages_Then_EnsureWelcomeMessage_Should_Reinsert_When_All_Deleted()
    {
        // 复现用户反馈场景：清理已读消息后，EnsureWelcomeMessage 会重新插入欢迎消息
        // 这是"首次感知"的预期行为——当用户没有任何消息时，重新注入欢迎消息让功能可被发现
        _service.EnsureWelcomeMessage();
        Assert.Single(_service.GetMessages());

        _service.MarkAllAsRead();
        // 模拟 ClearReadMessages：删除所有已读消息
        var all = _service.GetMessages();
        foreach (var m in all) _service.Delete(m.Id);
        Assert.Empty(_service.GetMessages());

        // 此时 EnsureWelcomeMessage（在登录/会话恢复时调用）会重新插入
        _service.EnsureWelcomeMessage();
        var reloaded = _service.GetMessages();
        Assert.Single(reloaded);
        Assert.False(reloaded[0].IsRead); // 新插入的是未读
    }

    public void Dispose() { }
}
