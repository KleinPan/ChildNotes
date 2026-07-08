using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

/// <summary>
/// 应用内消息中心 ViewModel。
/// 展示消息列表，支持标记已读/全部已读/删除。
/// </summary>
public partial class InAppMessageViewModel : ViewModelBase
{
    private readonly InAppMessageService _msgService = ServiceProvider.Instance.InAppMessageService;

    /// <summary>消息列表（按时间倒序）。</summary>
    public ObservableCollection<InAppMessage> Messages { get; } = new();

    /// <summary>是否有未读消息。</summary>
    [ObservableProperty] private bool _hasUnread;

    /// <summary>是否正在加载。</summary>
    [ObservableProperty] private bool _isLoading;

    /// <summary>未读消息变化通知（供 MineViewModel 更新未读数显示）。</summary>
    public event Action? UnreadCountChanged;

    public InAppMessageViewModel()
    {
        Title = "应用消息";
    }

    /// <summary>加载消息列表。</summary>
    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            // 注意：EnsureWelcomeMessage 不在此处调用，避免"清理已读消息"后再次进入时重复注入。
            // 欢迎消息仅在登录成功/会话恢复时注入一次。
            // 清理 30 天前的已读消息
            _msgService.CleanupOldReadMessages();

            var list = await Task.Run(() => _msgService.GetMessages());
            Messages.Clear();
            foreach (var m in list) Messages.Add(m);
            UpdateUnreadStatus();
        }
        catch (Exception ex)
        {
            DevLogger.Log("InAppMsg", $"LoadAsync failed: {ex.Message}");
            DisplayToast("加载消息失败");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>点击消息：标记已读（并触发未读数刷新）。</summary>
    [RelayCommand]
    private void TapMessage(InAppMessage msg)
    {
        if (!msg.IsRead)
        {
            _msgService.MarkAsRead(msg.Id);
            msg.IsRead = true;
            UpdateUnreadStatus();
        }
        // TODO: 后续根据 msg.Category 跳转到对应页面
        // general=无跳转, family_joined=Family, ai_report=AiAnalysis, points=Points
    }

    /// <summary>全部标记已读。</summary>
    [RelayCommand]
    private void MarkAllRead()
    {
        _msgService.MarkAllAsRead();
        foreach (var m in Messages) m.IsRead = true;
        UpdateUnreadStatus();
        DisplayToast("已全部标记为已读");
    }

    /// <summary>删除指定消息。</summary>
    [RelayCommand]
    private void DeleteMessage(InAppMessage msg)
    {
        _msgService.Delete(msg.Id);
        Messages.Remove(msg);
        UpdateUnreadStatus();
    }

    /// <summary>清理全部已读消息（保留未读）。</summary>
    [RelayCommand]
    private void ClearReadMessages()
    {
        var readMessages = Messages.Where(m => m.IsRead).ToList();
        foreach (var m in readMessages)
        {
            _msgService.Delete(m.Id);
            Messages.Remove(m);
        }
        if (readMessages.Count > 0)
        {
            DisplayToast($"已清理 {readMessages.Count} 条已读消息");
        }
        else
        {
            DisplayToast("没有已读消息可清理");
        }
    }

    private void UpdateUnreadStatus()
    {
        var unread = _msgService.GetUnreadCount();
        HasUnread = unread > 0;
        UnreadCountChanged?.Invoke();
    }
}
