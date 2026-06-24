using Avalonia.Controls;
using Avalonia.Interactivity;
using ChildNotes.ViewModels;

namespace ChildNotes.Views;

public partial class PointsView : UserControl
{
    public PointsView()
    {
        InitializeComponent();
    }

    private void OnBack(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PointsViewModel vm) vm.Back();
    }
}
