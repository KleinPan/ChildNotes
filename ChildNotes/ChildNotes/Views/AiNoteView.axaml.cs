using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using ChildNotes.Infrastructure;

namespace ChildNotes.Views;

public partial class AiNoteView : UserControl
{
    private const double DefaultMaxHeight = 600;

    // 上一次应用的键盘偏移量
    private double _lastKbOffset;

    public AiNoteView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        AddHandler(InputElement.GotFocusEvent, OnGotFocus, RoutingStrategies.Bubble);
        AddHandler(InputElement.LostFocusEvent, OnLostFocus, RoutingStrategies.Bubble);

        // ★ 使用 Avalonia 内置 InputPane API 监听键盘（跨平台，坐标系统一为 lp）
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.InputPane != null)
        {
            topLevel.InputPane.StateChanged += OnInputPaneStateChanged;
            DevLogger.Log("AiNoteView", "Attached: InputPane listener registered");
        }

        DevLogger.Log("AiNoteView", "Attached: listeners added");
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        RemoveHandler(InputElement.GotFocusEvent, OnGotFocus);
        RemoveHandler(InputElement.LostFocusEvent, OnLostFocus);

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.InputPane != null)
        {
            topLevel.InputPane.StateChanged -= OnInputPaneStateChanged;
        }

        DevLogger.Log("AiNoteView", "Detached: listeners removed");
    }

    /// <summary>
    /// ★ 核心回调：Avalonia InputPane 键盘状态变化。
    /// 使用 Avalonia 内置 API，坐标系统一为 lp（无需 px→lp 转换），
    /// 不依赖 windowSoftInputMode，跨平台兼容。
    /// </summary>
    private void OnInputPaneStateChanged(object? sender, InputPaneStateEventArgs e)
    {
        if (SheetRoot is null || !IsVisible) return;

        if (e.NewState == InputPaneState.Closed)
        {
            // 键盘收回：清除偏移，弹窗回弹
            ClearKeyboardOffset(reason: "InputPane.Closed");
            return;
        }

        if (e.NewState == InputPaneState.Open && e.EndRect.Height > 0)
        {
            // 键盘弹出：e.EndRect.Height 就是精确的键盘高度（lp 单位）
            ApplyKeyboardOffset(e.EndRect.Height, reason: "InputPane.Open");
        }
    }

    private void OnGotFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TextBox tb)
        {
            DispatcherTimer.RunOnce(() => ScrollFocusedIntoView(tb), TimeSpan.FromMilliseconds(100));
        }
    }

    private void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        // LostFocus 不再主动清除偏移——由 InputPane.Closed 统一处理
        // 这避免了 200ms 延迟导致的时序问题
    }

    /// <summary>
    /// 应用键盘偏移：通过 Margin.Bottom 将弹窗推到键盘上方。
    ///
    /// 使用 Avalonia InputPane 的 EndRect.Height 作为键盘高度，
    /// 坐标系与 Avalonia UI 完全一致（都是 lp），无转换误差。
    /// </summary>
    private void ApplyKeyboardOffset(double keyboardHeightLp, string reason)
    {
        var parent = SheetRoot.Parent as Layoutable;
        var containerHeight = parent?.Bounds.Height ?? 0;

        // 安全上限：不超过容器高度的 90%
        var maxOffset = containerHeight > 0 ? containerHeight * 0.9 : 800;
        var offset = Math.Min(keyboardHeightLp, maxOffset);

        SheetRoot.Margin = new Thickness(0, 0, 0, offset);

        if (containerHeight > 0)
        {
            SheetRoot.MaxHeight = Math.Max(280, containerHeight - offset);
        }

        _lastKbOffset = offset;

        DevLogger.Log("AiNoteView",
            $"ApplyOffset | {reason} | kbH={keyboardHeightLp:F1}lp | offset={offset:F1}lp | " +
            $"containerH={containerHeight:F1}lp | MaxH={SheetRoot.MaxHeight:F0} | " +
            $"sheetY={SheetRoot.Bounds.Y:F0} | sheetBottom={SheetRoot.Bounds.Y + SheetRoot.Bounds.Height:F0}");
    }

    /// <summary>清除键盘偏移，恢复默认状态。</summary>
    private void ClearKeyboardOffset(string reason)
    {
        if (SheetRoot is null) return;
        SheetRoot.Margin = new Thickness(0);
        SheetRoot.MaxHeight = DefaultMaxHeight;
        _lastKbOffset = 0;

        DevLogger.Log("AiNoteView", $"ClearOffset | {reason}");
    }

    private void ScrollFocusedIntoView(TextBox tb)
    {
        try
        {
            if (ContentScroll is null) return;
            var bounds = tb.Bounds;
            tb.BringIntoView(new Rect(0, -12, bounds.Width, bounds.Height));
        }
        catch (Exception ex)
        {
            DevLogger.Log("AiNoteView", $"BringIntoView failed: {ex.Message}");
        }
    }
}
