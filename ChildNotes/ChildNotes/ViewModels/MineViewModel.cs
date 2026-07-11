using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

public partial class MineViewModel : ViewModelBase, IActivatable
{
    private readonly AuthService _auth = ServiceProvider.Instance.AuthService;
    private readonly BabyService _babyService = ServiceProvider.Instance.BabyService;
    private readonly InAppMessageService _msgService = ServiceProvider.Instance.InAppMessageService;

    [ObservableProperty] private string _nickName = string.Empty;
    [ObservableProperty] private string _avatarUrl = string.Empty;
    [ObservableProperty] private string _roleText = "家长";
    [ObservableProperty] private int _babyCount;

    /// <summary>是否有未读应用内消息（控制 MineView 红点显示）。</summary>
    [ObservableProperty] private bool _hasUnreadMessages;

    /// <summary>未读消息数（控制 MineView 红点数字）。</summary>
    [ObservableProperty] private int _unreadMessageCount;

    /// <summary>
    /// 开发者选项入口是否可见。仅开发版构建可见，正式版隐藏入口。
    /// 由 <see cref="BuildConfiguration.IsDevelopmentBuild"/> 编译时决定，运行时恒定。
    /// </summary>
    public bool IsDeveloperOptionsVisible => BuildConfiguration.IsDevelopmentBuild;

    /// <summary>
    /// 应用版本号（从程序集 InformationalVersion 读取；CI 构建时由 release workflow 用 tag 名覆盖版本号）
    /// </summary>
    public string AppVersion
    {
        get
        {
            var attr = (System.Reflection.AssemblyInformationalVersionAttribute[])
                System.Attribute.GetCustomAttributes(
                    System.Reflection.Assembly.GetExecutingAssembly(),
                    typeof(System.Reflection.AssemblyInformationalVersionAttribute));
            var ver = attr.Length > 0 ? attr[0].InformationalVersion : "0.0.0";
            return $"v{ver}";
        }
    }

    public ObservableCollection<Baby> BabyList { get; } = new();

    public event Action? LogoutRequested;

    public void Activate()
    {
        var user = _auth.CurrentUser;
        NickName = user?.NickName ?? "未登录";
        AvatarUrl = user?.AvatarUrl ?? string.Empty;

        BabyList.Clear();
        var babies = _babyService.LoadBabyList();
        foreach (var b in babies) BabyList.Add(b);
        BabyCount = babies.Count;

        // 刷新未读消息数（用于"我的"页红点显示）
        RefreshUnreadMessages();
    }

    /// <summary>刷新未读消息数（由 InAppMessageViewModel 关闭后回调）。</summary>
    public void RefreshUnreadMessages()
    {
        try
        {
            var count = _msgService.GetUnreadCount();
            UnreadMessageCount = count;
            HasUnreadMessages = count > 0;
        }
        catch { /* 非致命 */ }
    }

    [RelayCommand]
    private void Logout()
    {
        _auth.Logout();
        LogoutRequested?.Invoke();
    }
}
