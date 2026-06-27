using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ChildNotes.ViewModels;

namespace ChildNotes.Views;

public partial class SyncSettingsView : UserControl
{
    public SyncSettingsView()
    {
        InitializeComponent();
    }

    private void OnBackTap(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is SyncSettingsViewModel vm) vm.BackCommand.Execute(null);
    }
}
