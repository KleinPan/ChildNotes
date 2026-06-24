using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ChildNotes.Models;
using ChildNotes.ViewModels;

namespace ChildNotes.Views;

public partial class AiAnalysisView : UserControl
{
    public AiAnalysisView()
    {
        InitializeComponent();
    }

    private void OnBack(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AiAnalysisViewModel vm) vm.Back();
    }

    private void OnCloseConfig(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is AiAnalysisViewModel vm) vm.ShowConfigSheet = false;
    }

    private void OnRecordTap(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.Tag is long id && DataContext is AiAnalysisViewModel vm)
        {
            var record = FindRecord(vm, id);
            if (record is not null) vm.OpenDetail(record);
        }
    }

    private static AiAnalysisRecord? FindRecord(AiAnalysisViewModel vm, long id)
    {
        foreach (var r in vm.Records)
        {
            if (r.Id == id) return r;
        }
        return null;
    }
}
