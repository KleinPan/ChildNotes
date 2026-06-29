using Avalonia;
using Avalonia.Controls;

namespace ChildNotes.Controls;

/// <summary>
/// 统一 Toast 浮层控件：绑定到 ViewModelBase 的 ShowToast / ToastMessage 即可。
/// 替代各 View 中重复的底部居中半透明 Border+TextBlock 模板。
/// </summary>
public partial class ToastOverlay : UserControl
{
    /// <summary>是否显示 Toast（绑定到 ViewModelBase.ShowToast）。</summary>
    public static readonly StyledProperty<bool> ShowToastProperty =
        AvaloniaProperty.Register<ToastOverlay, bool>(nameof(ShowToast));

    /// <summary>Toast 文本（绑定到 ViewModelBase.ToastMessage）。</summary>
    public static readonly StyledProperty<string?> ToastMessageProperty =
        AvaloniaProperty.Register<ToastOverlay, string?>(nameof(ToastMessage));

    public ToastOverlay()
    {
        InitializeComponent();
    }

    public bool ShowToast
    {
        get => GetValue(ShowToastProperty);
        set => SetValue(ShowToastProperty, value);
    }

    public string? ToastMessage
    {
        get => GetValue(ToastMessageProperty);
        set => SetValue(ToastMessageProperty, value);
    }
}
