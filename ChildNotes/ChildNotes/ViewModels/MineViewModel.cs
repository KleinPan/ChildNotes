using System.Collections.ObjectModel;
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
        try
        {
            DevLogger.Log("Mine", "Activate start");
            var user = _auth.CurrentUser;
            // 登录态以 sync_config.CloudUserId 为准（IsLoggedIn），不依赖 app_user 表的 profile 缓存。
            // 若已登录但 app_user 表无 CloudUserId 行（db 重建/旧版未 Upsert 等场景），
            // 显示占位文案"已登录"，避免误显示"未登录"；下次同步或拉取 profile 后会刷新。
            if (user is not null)
            {
                NickName = user.NickName;
                AvatarUrl = user.AvatarUrl;
            }
            else if (_auth.IsLoggedIn)
            {
                NickName = _locale.GetString("Mine_LoggedIn", "已登录");
                AvatarUrl = string.Empty;
            }
            else
            {
                NickName = _locale.GetString("Mine_NotLoggedIn", "未登录");
                AvatarUrl = string.Empty;
            }

            // DB 调用 + HTTP 调用全部移到后台线程，避免 UI 线程阻塞。
            // 历史问题：HttpClient.SendAsync 在 Android 上首次调用会做 DNS 解析 + SSL 握手，
            // 部分操作可能在 UI 线程同步执行，导致 ANR。
            _ = LoadDataAsync();
            DevLogger.Log("Mine", "Activate done (async loading scheduled)");
        }
        catch (Exception ex)
        {
            DevLogger.Log("Mine", "Activate EXCEPTION: " + ex);
            ReleaseLogger.Error("Mine", ex, "Activate failed");
        }
    }

    /// <summary>加载取消令牌：快速切 tab 时取消旧加载，防止旧数据覆盖新 UI。</summary>
    private CancellationTokenSource? _loadCts;

    /// <summary>
    /// 后台加载宝宝列表 + 未读消息 + 会员状态。
    /// 所有 DB/HTTP 调用都在 Task.Run 内执行，UI 线程仅做 ObservableCollection/属性赋值。
    /// 含 CTS 取消：快速切 tab 时取消旧加载。
    /// </summary>
    private async Task LoadDataAsync()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        try
        {
            // 1. 后台线程：DB 查询
            var babies = await Task.Run(() => _babyService.LoadBabyList(), ct);
            var unreadCount = await Task.Run(() =>
            {
                try { return _msgService.GetUnreadCount(); }
                catch { return 0; }
            }, ct);

            ct.ThrowIfCancellationRequested();
            // 2. UI 线程：更新 ObservableCollection（跨线程访问会抛异常）
            BabyList.Clear();
            foreach (var b in babies) BabyList.Add(b);
            BabyCount = babies.Count;
            OnPropertyChanged(nameof(BabyCountText));
            UnreadMessageCount = unreadCount;
            HasUnreadMessages = unreadCount > 0;

            // 3. 后台线程：HTTP 查询会员状态
            await RefreshMembershipStatusAsync();
        }
        catch (OperationCanceledException)
        {
            // 被新加载取代，静默
        }
        catch (Exception ex)
        {
            DevLogger.Log("Mine", "LoadDataAsync EXCEPTION: " + ex);
            ReleaseLogger.Error("Mine", ex, "LoadDataAsync failed");
        }
        finally
        {
            _loadCts?.Dispose();
            _loadCts = null;
        }
    }

    /// <summary>从后端拉取会员状态并刷新文案。会员中心关闭后由 MainShellViewModel 调用。</summary>
    public async Task RefreshMembershipStatusAsync()
    {
        var activeText = _locale.GetString("Mine_Membership_Active", "会员");
        var regularText = _locale.GetString("Mine_Membership_Regular", "普通用户");
        try
        {
            // HTTP 调用包到 Task.Run，确保 DNS/SSL 握手在后台线程执行
            var status = await Task.Run(() => ServiceProvider.Instance.MembershipApiClient.GetStatusAsync());
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

    /// <summary>刷新未读消息数（由 InAppMessageViewModel 关闭后回调）。DB 查询移到后台线程。</summary>
    public async Task RefreshUnreadMessagesAsync()
    {
        try
        {
            var count = await Task.Run(() => _msgService.GetUnreadCount());
            UnreadMessageCount = count;
            HasUnreadMessages = count > 0;
        }
        catch { /* 非致命 */ }
    }

    [RelayCommand]
    private async Task Logout()
    {
        // v5：异步登出（清理 SecureStorage + sync_config.cloud_user_id），保留业务数据
        try
        {
            await _auth.LogoutAsync();
        }
        catch (Exception ex)
        {
            DevLogger.Log("Mine", "LogoutAsync failed: " + ex.Message);
        }
        LogoutRequested?.Invoke();
    }
}
