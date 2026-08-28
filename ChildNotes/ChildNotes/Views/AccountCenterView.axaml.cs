using Avalonia.Controls;

namespace ChildNotes.Views;

/// <summary>
/// "账户中心"页 code-behind。
/// 会员中心/积分任务入口已提级到"我的"页，本页仅保留登录/退出登录（按钮直接绑定 ViewModel 命令，无事件转发）。
/// </summary>
public partial class AccountCenterView : UserControl
{
    public AccountCenterView()
    {
        InitializeComponent();
    }
}
