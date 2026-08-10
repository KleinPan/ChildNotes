using Avalonia.Controls;
using Avalonia.Input;
using ChildNotes.Models;
using ChildNotes.ViewModels;

namespace ChildNotes.Views;

public partial class AiAnalysisView : UserControl
{
    public AiAnalysisView()
    {
        InitializeComponent();
    }

    private void OnRecordTap(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.Tag is string id && DataContext is AiAnalysisViewModel vm)
        {
            var record = FindRecord(vm, id);
            if (record is not null) vm.OpenDetail(record);
        }
    }

    /// <summary>
    /// 详情头点击：点击除"收起按钮"之外的区域也折叠返回列表。
    /// </summary>
    private void OnDetailHeaderTap(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is AiAnalysisViewModel vm && vm.BackToListCommand.CanExecute(null))
        {
            vm.BackToListCommand.Execute(null);
        }
    }

    private static AiAnalysisRecord? FindRecord(AiAnalysisViewModel vm, string id)
    {
        foreach (var r in vm.Records)
        {
            if (r.Id == id) return r;
        }
        return null;
    }
}
