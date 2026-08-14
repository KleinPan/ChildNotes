using Avalonia.Controls;
using Avalonia.Input;
using ChildNotes.ViewModels;

namespace ChildNotes.Views;

/// <summary>
/// "账户中心"页 code-behind：将 PointerPressed 事件转发为 ViewModel 命令。
/// 与 MineView 的事件转发模式一致（MineView 也是 PointerPressed → 调用 shell 方法）。
/// </summary>
public partial class AccountCenterView : UserControl
{
    public AccountCenterView()
    {
        InitializeComponent();
    }

    private void OnMembershipTap(object? sender, PointerPressedEventArgs e)
        => (DataContext as AccountCenterViewModel)?.OpenMembershipCommand.Execute(null);

    private void OnPointsTap(object? sender, PointerPressedEventArgs e)
        => (DataContext as AccountCenterViewModel)?.OpenPointsCommand.Execute(null);
}
