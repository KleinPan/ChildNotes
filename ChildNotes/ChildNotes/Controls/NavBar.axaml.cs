using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace ChildNotes.Controls;

/// <summary>
/// 统一顶栏控件：左侧返回按钮 + 中间标题 + 右侧自定义内容。
/// 替代各 View 中重复的 Border.nav-bar 三列 Grid 模板。
/// 调用方：<c>&lt;ctrl:NavBar Title="..." BackCommand="{Binding BackCommand}" /&gt;</c>
/// </summary>
public partial class NavBar : UserControl
{
    /// <summary>是否显示左侧返回按钮（默认 true）。</summary>
    public static readonly StyledProperty<bool> ShowBackProperty =
        AvaloniaProperty.Register<NavBar, bool>(nameof(ShowBack), defaultValue: true);

    /// <summary>顶栏标题文本。</summary>
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<NavBar, string?>(nameof(Title));

    /// <summary>右侧自定义内容（按钮、文本等）。</summary>
    public static readonly StyledProperty<object?> RightContentProperty =
        AvaloniaProperty.Register<NavBar, object?>(nameof(RightContent));

    /// <summary>
    /// 返回按钮命令。由调用方显式传入（通常 <c>BackCommand="{Binding BackCommand}"</c>），
    /// 不再通过反射访问外部 DataContext。
    /// </summary>
    public static readonly StyledProperty<ICommand?> BackCommandProperty =
        AvaloniaProperty.Register<NavBar, ICommand?>(nameof(BackCommand));

    public NavBar()
    {
        InitializeComponent();
    }

    public bool ShowBack
    {
        get => GetValue(ShowBackProperty);
        set => SetValue(ShowBackProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public object? RightContent
    {
        get => GetValue(RightContentProperty);
        set => SetValue(RightContentProperty, value);
    }

    public ICommand? BackCommand
    {
        get => GetValue(BackCommandProperty);
        set => SetValue(BackCommandProperty, value);
    }
}
