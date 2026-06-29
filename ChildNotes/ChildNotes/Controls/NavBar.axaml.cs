using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;

namespace ChildNotes.Controls;

/// <summary>
/// 统一顶栏控件：左侧返回按钮 + 中间标题 + 右侧自定义内容。
/// 替代各 View 中重复的 Border.nav-bar 三列 Grid 模板。
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

    /// <summary>点击返回时触发（若未绑定 BackCommand 则使用本事件）。</summary>
    public event EventHandler? BackClicked;

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

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        // 如果 DataContext 实现了 BackCommand（ViewModelBase），由绑定处理
        // 否则触发 BackClicked 事件供 View code-behind 处理
        BackClicked?.Invoke(this, e);
    }
}
