using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace ChildNotes.Controls;

/// <summary>
/// 统一确认对话框控件：模态遮罩 + 居中白底卡片 + 标题 / 消息 / 取消&amp;确认按钮。
/// 替代各 View 中重复的 Border(mask)+Border(card)+StackPanel 模板。
/// </summary>
public partial class DialogHost : UserControl
{
    /// <summary>是否显示对话框。</summary>
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<DialogHost, bool>(nameof(IsOpen));

    /// <summary>对话框标题文本。</summary>
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<DialogHost, string?>(nameof(Title));

    /// <summary>取消按钮文本（默认“取消”）。</summary>
    public static readonly StyledProperty<string> CancelTextProperty =
        AvaloniaProperty.Register<DialogHost, string>(nameof(CancelText), defaultValue: "取消");

    /// <summary>确认按钮文本（默认“确认”）。</summary>
    public static readonly StyledProperty<string> ConfirmTextProperty =
        AvaloniaProperty.Register<DialogHost, string>(nameof(ConfirmText), defaultValue: "确认");

    /// <summary>可选：自定义消息内容（支持 Run 内联粗体等复杂文本）。</summary>
    public static readonly StyledProperty<object?> MessageContentProperty =
        AvaloniaProperty.Register<DialogHost, object?>(nameof(MessageContent));

    /// <summary>取消按钮命令。</summary>
    public static readonly StyledProperty<ICommand?> CancelCommandProperty =
        AvaloniaProperty.Register<DialogHost, ICommand?>(nameof(CancelCommand));

    /// <summary>确认按钮命令。</summary>
    public static readonly StyledProperty<ICommand?> ConfirmCommandProperty =
        AvaloniaProperty.Register<DialogHost, ICommand?>(nameof(ConfirmCommand));

    public DialogHost()
    {
        InitializeComponent();
    }

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string CancelText
    {
        get => GetValue(CancelTextProperty);
        set => SetValue(CancelTextProperty, value);
    }

    public string ConfirmText
    {
        get => GetValue(ConfirmTextProperty);
        set => SetValue(ConfirmTextProperty, value);
    }

    public object? MessageContent
    {
        get => GetValue(MessageContentProperty);
        set => SetValue(MessageContentProperty, value);
    }

    public ICommand? CancelCommand
    {
        get => GetValue(CancelCommandProperty);
        set => SetValue(CancelCommandProperty, value);
    }

    public ICommand? ConfirmCommand
    {
        get => GetValue(ConfirmCommandProperty);
        set => SetValue(ConfirmCommandProperty, value);
    }
}
