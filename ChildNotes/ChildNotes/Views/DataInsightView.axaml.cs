using Avalonia.Controls;
using Avalonia.Input;
using ChildNotes.ViewModels;

namespace ChildNotes.Views;

/// <summary>"数据洞察"页 code-behind：PointerPressed → ViewModel 命令。</summary>
public partial class DataInsightView : UserControl
{
    public DataInsightView()
    {
        InitializeComponent();
    }

    private void OnStatisticsTap(object? sender, PointerPressedEventArgs e)
        => (DataContext as DataInsightViewModel)?.OpenStatisticsCommand.Execute(null);

    private void OnAiAnalysisTap(object? sender, PointerPressedEventArgs e)
        => (DataContext as DataInsightViewModel)?.OpenAiAnalysisCommand.Execute(null);
}
