using Avalonia.Controls;
using ChildNotes.Data;

namespace ChildNotes.Views;

/// <summary>
/// 启动 loading 视图：显示育儿小知识 + 进度条。
/// 系统启动屏已显示应用图标，此处不重复，专注于展示有价值的育儿知识。
/// 育儿知识在构造函数即设置，立即可见不依赖动画，无论显示多久都能看到内容。
/// </summary>
public partial class LoadingView : UserControl
{
    public LoadingView()
    {
        InitializeComponent();
        // 构造时立即设置育儿小知识，不依赖 Loaded 事件
        TipText.Text = ParentingTips.GetRandomTip();
    }
}
