using CommunityToolkit.Mvvm.Input;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

/// <summary>
/// "账户中心"页 ViewModel：从"我的"页顶部用户卡进入。
/// 收纳账户资产类入口（会员/积分）与退出登录，本身不含业务逻辑，
/// 子页跳转通过事件回调 MainShellViewModel 执行（保持与现有 OpenXxx 模式一致）。
/// </summary>
public partial class AccountCenterViewModel : ViewModelBase
{
    private readonly LocaleManager _locale = LocaleManager.Instance;

    /// <summary>请求打开会员中心页。</summary>
    public event Action? OpenMembershipRequested;

    /// <summary>请求打开积分任务页。</summary>
    public event Action? OpenPointsRequested;

    /// <summary>请求退出登录（由 MainShellViewModel 订阅，复用 Mine.LogoutCommand 链路）。</summary>
    public event Action? LogoutRequested;

    public AccountCenterViewModel()
    {
        Title = _locale.GetString("Account_Title", "账户中心");
    }

    [RelayCommand] private void OpenMembership() => OpenMembershipRequested?.Invoke();
    [RelayCommand] private void OpenPoints() => OpenPointsRequested?.Invoke();
    [RelayCommand] private void Logout() => LogoutRequested?.Invoke();
}
