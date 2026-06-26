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
        if (DataContext is MainShellViewModel vm)
        {
            DevLogger.Log("Shell", "MainShellView ctor: DataContext already set, calling ActivateHome");
            vm.ActivateHome();
        }
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
