using ChildNotes.Data.Repositories;
using ChildNotes.Models;

namespace ChildNotes.Services;

/// <summary>
/// 应用内消息服务：业务逻辑封装层。
///
/// 职责：
/// - 拉取用户消息（本地 SQLite）
/// - 标记已读（单条/全部）
/// - 注入示例消息（首次启动时生成欢迎消息，便于用户感知功能存在）
/// - 清理过期已读消息
///
/// 后续接入推送时，推送消息落地后也通过本服务查询展示。
/// </summary>
public sealed class InAppMessageService
{
    private readonly InAppMessageRepository _repo;
    private readonly AppState _state;

    public InAppMessageService(InAppMessageRepository repo, AppState state)
    {
        _repo = repo;
        _state = state;
    }

    /// <summary>查询当前用户的消息列表（按时间倒序）。</summary>
    public List<InAppMessage> GetMessages(int limit = 100)
    {
        var uid = _state.User?.Id;
        if (string.IsNullOrEmpty(uid)) return new();
        return _repo.GetByUser(uid, limit);
    }

    /// <summary>当前用户未读消息数。</summary>
    public int GetUnreadCount()
    {
        var uid = _state.User?.Id;
        if (string.IsNullOrEmpty(uid)) return 0;
        return _repo.CountUnread(uid);
    }

    /// <summary>标记单条消息为已读。</summary>
    public void MarkAsRead(string messageId) => _repo.MarkAsRead(messageId);

    /// <summary>标记当前用户全部消息为已读。</summary>
    public void MarkAllAsRead()
    {
        var uid = _state.User?.Id;
        if (string.IsNullOrEmpty(uid)) return;
        _repo.MarkAllAsRead(uid);
    }

    /// <summary>删除指定消息。</summary>
    public void Delete(string messageId) => _repo.Delete(messageId);

    /// <summary>清理 30 天前的已读消息。</summary>
    public int CleanupOldReadMessages()
    {
        var uid = _state.User?.Id;
        if (string.IsNullOrEmpty(uid)) return 0;
        return _repo.CleanupOldReadMessages(uid, 30);
    }

    /// <summary>
    /// 插入一条本地消息（用于应用内通知，如家庭成员加入提示）。
    /// 后端推送消息落地时也可调用此方法。
    /// </summary>
    public void Insert(InAppMessage msg)
    {
        if (string.IsNullOrEmpty(msg.Id)) msg.Id = Guid.NewGuid().ToString("N");
        if (string.IsNullOrEmpty(msg.CreatedAt)) msg.CreatedAt = DateTime.UtcNow.ToString("O");
        _repo.Insert(msg);
    }

    /// <summary>
    /// 首次启动时注入一条欢迎消息，便于用户感知"应用消息"功能。
    /// 通过查询消息总数判断是否首次。
    /// </summary>
    public void EnsureWelcomeMessage()
    {
        var uid = _state.User?.Id;
        if (string.IsNullOrEmpty(uid)) return;
        var existing = _repo.GetByUser(uid, limit: 1);
        if (existing.Count > 0) return;

        var welcome = new InAppMessage
        {
            Id = $"welcome-{uid}",
            UserId = uid,
            Title = "欢迎使用 ChildNotes",
            Body = "这里会展示应用内的通知消息，如家庭成员加入、AI 报告生成完成等。点击消息可跳转到对应功能。",
            Category = "general",
            DataJson = "{}",
            IsRead = false,
            CreatedAt = DateTime.UtcNow.ToString("O")
        };
        _repo.Insert(welcome);
    }
}
