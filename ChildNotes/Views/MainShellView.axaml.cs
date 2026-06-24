using Avalonia.Controls;
using ChildNotes.ViewModels;

namespace ChildNotes.Views;

public partial class MainShellView : UserControl
{
    public MainShellView()
    {
        InitializeComponent();
        if (DataContext is MainShellViewModel vm)
        {
            vm.ActivateHome();
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainShellViewModel vm)
        {
            vm.ActivateHome();
        }
    }
}
