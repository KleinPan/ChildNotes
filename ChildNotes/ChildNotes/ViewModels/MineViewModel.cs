using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Services;
using ChildNotes.Shared.Constants;

namespace ChildNotes.ViewModels;

public partial class MineViewModel : ViewModelBase, IActivatable
{
    private readonly AuthService _auth = ServiceProvider.Instance.AuthService;
    private readonly BabyService _babyService = ServiceProvider.Instance.BabyService;
    private readonly InAppMessageService _msgService = ServiceProvider.Instance.InAppMessageService;
    private readonly LocaleManager _locale = LocaleManager.Instance;

    [ObservableProperty] private string _nickName = string.Empty;
    [ObservableProperty] private string _avatarUrl = string.Empty;
    [ObservableProperty] private string _roleText = string.Empty;
    [ObservableProperty] private int _babyCount;

    /// <summary>会员状态文案（"会员"/"普通用户"），异步从后端加载。</summary>
    [ObservableProperty] private string _membershipStatusText = string.Empty;

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
    /// 宝宝数量展示文案（"3个宝宝" / "3 babies"）。语言切换时随翻译后缀刷新。
    /// </summary>
    public string BabyCountText => $"{BabyCount}{_locale.GetString("Mine_BabyCount_Suffix", "个宝宝")}";

    /// <summary>
    /// 当前语言显示名（在 MineView 语言入口右侧展示，如 "简体中文" / "English"）。
    /// </summary>
    public string LanguageDisplayText => _locale.CurrentLanguage == AppLanguage.En
        ? _locale.GetString("Language_En", "English")
        : _locale.GetString("Language_ZhHans", "简体中文");

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

    public MineViewModel()
    {
        // 语言切换时刷新所有依赖翻译的属性
        _locale.LanguageChanged += OnLanguageChanged;
        // 初始 RoleText
        RoleText = _locale.GetString("Mine_Role_Parent", "家长");
    }

    private void OnLanguageChanged(AppLanguage lang)
    {
        // 刷新依赖翻译的派生属性
        RoleText = _locale.GetString("Mine_Role_Parent", "家长");
        OnPropertyChanged(nameof(BabyCountText));
        OnPropertyChanged(nameof(LanguageDisplayText));
        // 会员状态文案重新计算
        _ = RefreshMembershipStatusAsync();
    }

    public void Activate()
    {
        // 用户信息从内存读取，立即同步设置（无 DB 调用，不会阻塞）
        var user = _auth.CurrentUser;
        NickName = user?.NickName ?? _locale.GetString("Mine_NotLoggedIn", "未登录");
        AvatarUrl = user?.AvatarUrl ?? string.Empty;

        // DB 调用移到后台线程，避免启动期与 TryRestoreSession 的 SQLite 操作竞态导致 ANR/闪退
        _ = ActivateCoreAsync();
    }

    private async Task ActivateCoreAsync()
    {
        // 后台线程执行 DB 查询，避免 UI 线程同步阻塞
        // 注意：不调 _babyService.LoadBabyList()，因为它会修改 AppState.BabyList（ObservableCollection），
        // 后台线程修改会触发其他 VM 的 CollectionChanged 跨线程异常。
        // 启动时 TryRestoreSession 已调过 LoadBabyList，此处只读当前快照即可。
        var babies = await Task.Run(() => _babyService.GetBabyListSnapshot());
        var unreadCount = 0;
        try { unreadCount = await Task.Run(() => _msgService.GetUnreadCount()); }
        catch { /* 非致命 */ }

        // UI 属性更新 marshalling 到 UI 线程（ObservableCollection 必须在 UI 线程修改）
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            BabyList.Clear();
            foreach (var b in babies) BabyList.Add(b);
            BabyCount = babies.Count;
            OnPropertyChanged(nameof(BabyCountText));

            UnreadMessageCount = unreadCount;
            HasUnreadMessages = unreadCount > 0;
        });

        // 异步刷新会员状态文案（网络调用，不阻塞 UI）
        _ = RefreshMembershipStatusAsync();
    }

    /// <summary>从后端拉取会员状态并刷新文案。会员中心关闭后由 MainShellViewModel 调用。</summary>
    public async Task RefreshMembershipStatusAsync()
    {
        var activeText = _locale.GetString("Mine_Membership_Active", "会员");
        var regularText = _locale.GetString("Mine_Membership_Regular", "普通用户");
        try
        {
            var status = await ServiceProvider.Instance.MembershipApiClient.GetStatusAsync();
            // MembershipStatusDto.ExpireAt 为 ISO 8601 字符串，解析为 DateTime? 后判断
            DateTime? expireAt = null;
            if (!string.IsNullOrEmpty(status?.ExpireAt) && DateTime.TryParse(status.ExpireAt, out var parsed))
                expireAt = parsed;
            MembershipStatusText = MembershipConstants.IsActive(expireAt) ? activeText : regularText;
        }
        catch
        {
            // 后端不可用时不阻塞 UI，显示本地缓存判断
            MembershipStatusText = MembershipConstants.IsActive(_auth.CurrentUser?.MembershipExpireAt) ? activeText : regularText;
        }
    }

    /// <summary>刷新未读消息数（由 InAppMessageViewModel 关闭后回调）。</summary>
    public void RefreshUnreadMessages()
    {
        _ = RefreshUnreadMessagesAsync();
    }

    private async Task RefreshUnreadMessagesAsync()
    {
        try
        {
            var count = await Task.Run(() => _msgService.GetUnreadCount());
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                UnreadMessageCount = count;
                HasUnreadMessages = count > 0;
            });
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
