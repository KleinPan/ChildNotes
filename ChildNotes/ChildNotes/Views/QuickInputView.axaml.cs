using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ChildNotes.Infrastructure;

namespace ChildNotes.Views;

/// <summary>
/// 首页底部快捷输入栏 code-behind。
/// 仅承载输入框焦点行为（用于触发软键盘弹出）。
///
/// 键盘上推逻辑已移至 MainShellView（通过给 Row=0 主内容区设底部 Margin 触发 Grid 重排），
/// 原因：QuickInputView 位于 Grid.Row=1(Auto) 内部，内部 RenderTransform 会被 Panel 边界裁剪。
/// </summary>
public partial class QuickInputView : UserControl
{
    public QuickInputView()
    {
        InitializeComponent();
        AddHandler(InputElement.GotFocusEvent, OnGotFocus, RoutingStrategies.Bubble);
        AddHandler(InputElement.LostFocusEvent, OnLostFocus, RoutingStrategies.Bubble);
    }

    private void OnGotFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TextBox)
        {
            DevLogger.Log("QuickInput", "TextBox GotFocus → keyboard should appear");
        }
    }

    private void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TextBox)
        {
            DevLogger.Log("QuickInput", "TextBox LostFocus");
        }
    }
}
