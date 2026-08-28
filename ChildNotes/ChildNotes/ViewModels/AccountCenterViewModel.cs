using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

/// <summary>
/// "账户中心"页 ViewModel：从"我的"页顶部用户卡进入。
/// 收纳登录/退出登录（会员中心/积分任务入口已提级到"我的"页），
/// 本身不含业务逻辑，子页跳转通过事件回调 MainShellViewModel 执行（保持与现有 OpenXxx 模式一致）。
/// v5 认证模型支持离线模式：未登录时显示"登录/注册"按钮，已登录时显示"退出登录"。
/// </summary>
public partial class AccountCenterViewModel : ViewModelBase
{
    private readonly LocaleManager _locale = LocaleManager.Instance;
    private readonly AuthService _auth = ServiceProvider.Instance.AuthService;

    /// <summary>请求退出登录（由 MainShellViewModel 订阅，复用 Mine.LogoutCommand 链路）。</summary>
    public event Action? LogoutRequested;

    /// <summary>请求打开登录页（未登录状态下由"登录/注册"按钮触发）。</summary>
    public event Action? OpenLoginRequested;

    /// <summary>是否已登录（CloudUserId 非空）。控制底部按钮显示"登录/注册"还是"退出登录"。</summary>
    [ObservableProperty] private bool _isLoggedIn;

    public AccountCenterViewModel()
    {
        Title = _locale.GetString("Account_Title", "账户中心");
        IsLoggedIn = _auth.IsLoggedIn;
    }

    /// <summary>每次打开账户中心时刷新登录状态（登出/登录后重新进入此页能正确显示）。</summary>
    public void RefreshLoginState()
    {
        IsLoggedIn = _auth.IsLoggedIn;
    }

    [RelayCommand] private void Logout() => LogoutRequested?.Invoke();

    /// <summary>未登录状态下点击"登录/注册"按钮，请求 App 切换到登录页。</summary>
    [RelayCommand] private void Login() => OpenLoginRequested?.Invoke();
}
