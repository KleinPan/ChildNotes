using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ChildNotes.Infrastructure;
using ChildNotes.ViewModels;

namespace ChildNotes.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        DevLogger.Log("HomeView", "ctor start");
        InitializeComponent();
        DevLogger.Log("HomeView", "ctor InitializeComponent done");
    }

    public static readonly IValueConverter ExpandTextConverter = new FuncValueConverter<bool, string>(
        isExpanded => isExpanded ? "收起" : "展开");

    // 箭头方向：展开时▼（向下，表示已展开/点击收起），收起时▲（向上，表示可向上展开）
    public static readonly IValueConverter ArrowTextConverter = new FuncValueConverter<bool, string>(
        isOpen => isOpen ? "▼" : "▲");

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        DevLogger.Log("HomeView", "OnAttachedToVisualTree");
    }

    private void OnQuickActionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is QuickActionItem item)
        {
            var shell = this.FindAncestorOfType<UserControl>();
            while (shell is not null && shell.DataContext is not MainShellViewModel)
            {
                shell = shell.FindAncestorOfType<UserControl>();
            }
            if (shell?.DataContext is MainShellViewModel vm)
            {
                vm.OpenQuickRecord(item.Type);
            }
        }
    }
}
