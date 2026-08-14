using CommunityToolkit.Mvvm.Input;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

/// <summary>
/// "数据洞察"页 ViewModel：从"我的"页"成长与数据"分组进入。
/// 收纳对宝宝历史数据的查看分析入口（统计图表 / AI 区间分析报告），
/// 两者底层对象都是宝宝记录数据，属同一业务域（见 interaction.md"我的"页二级页归属）。
/// 本身不含业务逻辑，子页跳转通过事件回调 MainShellViewModel 执行。
/// </summary>
public partial class DataInsightViewModel : ViewModelBase
{
    private readonly LocaleManager _locale = LocaleManager.Instance;

    /// <summary>请求打开统计图表页。</summary>
    public event Action? OpenStatisticsRequested;

    /// <summary>请求打开 AI 区间分析报告页。</summary>
    public event Action? OpenAiAnalysisRequested;

    public DataInsightViewModel()
    {
        Title = _locale.GetString("DataInsight_Title", "数据洞察");
    }

    [RelayCommand] private void OpenStatistics() => OpenStatisticsRequested?.Invoke();
    [RelayCommand] private void OpenAiAnalysis() => OpenAiAnalysisRequested?.Invoke();
}
