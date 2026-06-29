using Avalonia.Controls;
using Avalonia.Input;
using ChildNotes.ViewModels;

namespace ChildNotes.Views;

public partial class FeedingView : UserControl
{
    public FeedingView()
    {
        InitializeComponent();
    }

    private void OnRecordTap(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.Tag is RecordDisplayItem item && DataContext is FeedingViewModel vm)
        {
            vm.EditRecord(item);
        }
    }

    private void OnDeleteTap(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        if (sender is Border border && border.Tag is RecordDisplayItem item && DataContext is FeedingViewModel vm)
        {
            vm.RequestDelete(item);
        }
    }
}
