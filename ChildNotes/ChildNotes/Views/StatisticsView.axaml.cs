using Avalonia.Controls;
using Avalonia.Input;
using ChildNotes.ViewModels;

namespace ChildNotes.Views;

public partial class StatisticsView : UserControl
{
    public StatisticsView()
    {
        InitializeComponent();
    }

    private void OnTypeTap(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border b && b.DataContext is StatTypeOption opt && DataContext is StatisticsViewModel vm)
            vm.SelectTypeCommand.Execute(opt.Key);
    }

    private void OnRangeTap(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border b && b.DataContext is StatRangeOption opt && DataContext is StatisticsViewModel vm)
            vm.SelectRangeCommand.Execute(opt.Key);
    }
}
