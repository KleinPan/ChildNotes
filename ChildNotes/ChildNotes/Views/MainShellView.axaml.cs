using Avalonia.Controls;
using ChildNotes.Infrastructure;
using ChildNotes.ViewModels;

namespace ChildNotes.Views;

public partial class MainShellView : UserControl
{
    public MainShellView()
    {
        InitializeComponent();
        DevLogger.Log("Shell", "MainShellView ctor");
        // 不在此处调用 vm.ActivateHome()：OnDataContextChanged 会触发，
        // 在此处调用会导致启动时 ActivateHome 被执行 2-3 次（ctor + OnDataContextChanged + ActivateHomeAfterLogin），
        // 每次都触发 RefreshAsync（~1300ms DB 查询），白白浪费 ~2600ms。
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        DevLogger.Log("Shell", $"MainShellView.OnDataContextChanged: type={DataContext?.GetType().Name}");
        if (DataContext is MainShellViewModel vm)
        {
            vm.ActivateHome();
            DevLogger.Log("Shell", "MainShellView.OnDataContextChanged: ActivateHome done");
        }
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        DevLogger.Log("Shell", "MainShellView.OnAttachedToVisualTree");
    }
}
