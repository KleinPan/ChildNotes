using Avalonia.Controls;
using ChildNotes.Infrastructure;

namespace ChildNotes.Controls;

/// <summary>
/// 根容器：始终作为 ISingleViewApplicationLifetime.MainView 的唯一宿主。
/// 通过 ContentHost.Content 切换内部视图，避免安卓上替换 MainView 导致视觉树丢失。
/// </summary>
public partial class RootContainer : UserControl
{
    public RootContainer()
    {
        InitializeComponent();
        DevLogger.Log("Root", "RootContainer ctor");
    }

    /// <summary>设置当前显示的子视图</summary>
    public void SetContent(Control? view)
    {
        DevLogger.Log("Root", $"SetContent: {view?.GetType().Name ?? "null"}");
        ContentHost.Content = view;
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        DevLogger.Log("Root", $"OnAttachedToVisualTree, Parent={Parent?.GetType().Name ?? "null"}");
    }
}
