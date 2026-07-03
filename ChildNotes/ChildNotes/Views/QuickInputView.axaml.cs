using Avalonia.Controls;

namespace ChildNotes.Views;

/// <summary>
/// 首页底部快捷输入栏 code-behind：仅承载输入框焦点行为。
/// 按需求"点击输入框才聚焦"，不主动调用 Focus()。
/// </summary>
public partial class QuickInputView : UserControl
{
    public QuickInputView()
    {
        InitializeComponent();
    }
}
