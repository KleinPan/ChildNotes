using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ChildNotes.Infrastructure;
using ChildNotes.Services;

namespace ChildNotes.Views;

public partial class AiNoteView : UserControl
{
    private const double DefaultMaxHeight = 600;

    // 当前是否有 TextBox 处于焦点状态（用于判断是否要保持键盘规避）
    private bool _textBoxFocused;
    // 上一次应用的键盘偏移量（用于日志对比）
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
        KeyboardHeightProvider.HeightChanged += OnKeyboardHeightChanged;
        DevLogger.Log("AiNoteView", "Attached: GotFocus/LostFocus/KeyboardHeight listeners added");
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        RemoveHandler(InputElement.GotFocusEvent, OnGotFocus);
        RemoveHandler(InputElement.LostFocusEvent, OnLostFocus);
        KeyboardHeightProvider.HeightChanged -= OnKeyboardHeightChanged;
        DevLogger.Log("AiNoteView", "Detached: listeners removed");
    }

    private void OnKeyboardHeightChanged(double keyboardHeightLp)
    {
        // 桌面端不会触发此回调（KeyboardHeightService 只在安卓端注册），但防御性判断
        if (!OperatingSystem.IsAndroid()) return;

        // 无论是否 focused 都响应——用户可能在键盘已弹出时切换表单类型
        if (IsVisible)
        {
            ApplyKeyboardOffset(reason: $"NativeKeyboard height={keyboardHeightLp:F0}lp");
        }
        DevLogger.Log("AiNoteView", $"OnKeyboardHeightChanged | height={keyboardHeightLp:F0}lp | visible={IsVisible}");
    }

    private void OnGotFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TextBox tb)
        {
            _textBoxFocused = true;
            ApplyKeyboardOffset(reason: $"TextBox focused: {(tb.Name ?? tb.Text ?? "<empty>")}");
            DispatcherTimer.RunOnce(() => ScrollFocusedIntoView(tb), TimeSpan.FromMilliseconds(100));
        }
    }

    private void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TextBox)
        {
            _textBoxFocused = false;
            DispatcherTimer.RunOnce(() =>
            {
                if (!_textBoxFocused)
                {
                    ClearKeyboardOffset(reason: "TextBox lost focus (deferred)");
                }
            }, TimeSpan.FromMilliseconds(200));
        }
    }

    /// <summary>
    /// 核心修复：通过设置 SheetRoot 的 Margin.Bottom 将整个抽屉向上推移，
    /// 避开软键盘遮挡。同时限制 MaxHeight 作为安全网（防止长表单溢出到键盘区域）。
    /// 逻辑与 RecordSheetView.ApplyKeyboardOffset 保持一致。
    /// </summary>
    private void ApplyKeyboardOffset(string reason)
    {
        if (SheetRoot is null)
        {
            DevLogger.Log("AiNoteView", "ApplyOffset SKIP: SheetRoot is null");
            return;
        }

        // 桌面端无软键盘，跳过偏移逻辑（Windows 输入框获得焦点不会触发键盘，
        // 但 GotFocus 仍会触发本方法，需要在此拦截避免误上推到屏幕中间）
        if (!OperatingSystem.IsAndroid())
        {
            DevLogger.Log("AiNoteView", $"ApplyOffset SKIP: not Android (reason={reason})");
            return;
        }

        var kbHeight = KeyboardHeightProvider.CurrentHeight;
        var topLevel = TopLevel.GetTopLevel(this);
        var viewHeight = topLevel?.ClientSize.Height ?? 0;

        double offset;
        string offsetSource;
        if (kbHeight > 10)
        {
            // 有真实键盘高度：向上推 keyboardHeight，让抽屉紧贴键盘上方
            offset = kbHeight;
            offsetSource = "native";
        }
        else if (_textBoxFocused)
        {
            // 已聚焦但还没收到原生回调：先用保守估算（屏幕 40%），等原生值到达后再精确调整
            offset = viewHeight > 0 ? viewHeight * 0.40 : 350;
            offsetSource = "fallback";
        }
        else
        {
            return; // 无键盘、无焦点，不需要偏移
        }

        // 安全上限：抽屉上推后顶部不应超过屏幕 5% 位置（避免顶到屏幕最上方）
        var maxOffset = viewHeight > 0 ? viewHeight * 0.95 : 800;
        if (offset > maxOffset)
        {
            offset = maxOffset;
            offsetSource += "(clamped)";
        }

        // 应用底部 margin 将抽屉上推
        SheetRoot.Margin = new Thickness(0, 0, 0, offset);

        // 安全网：也限制 MaxHeight（对长表单有效）
        if (viewHeight > 0)
        {
            SheetRoot.MaxHeight = Math.Max(280, viewHeight - offset - 16);
        }

        DevLogger.Log("AiNoteView",
            $"ApplyOffset | {reason} | src={offsetSource} | kbH={kbHeight:F0}lp | " +
            $"offset={offset:F0}lp | viewH={viewHeight:F0} | MaxH={SheetRoot.MaxHeight:F0} | " +
            $"sheetH={SheetRoot.Bounds.Height:F0} | sheetY={SheetRoot.Bounds.Y:F0} | " +
            $"sheetBottom={SheetRoot.Bounds.Y + SheetRoot.Bounds.Height:F0}");
        _lastKbOffset = offset;
    }

    /// <summary>清除键盘偏移，恢复默认状态。</summary>
    private void ClearKeyboardOffset(string reason)
    {
        if (SheetRoot is null) return;
        SheetRoot.Margin = new Thickness(0);
        SheetRoot.MaxHeight = DefaultMaxHeight;
        _lastKbOffset = 0;
        DevLogger.Log("AiNoteView", $"ClearOffset | {reason} | restored default");
    }

    private void ScrollFocusedIntoView(TextBox tb)
    {
        try
        {
            if (ContentScroll is null) return;
            var bounds = tb.Bounds;
            tb.BringIntoView(new Avalonia.Rect(0, -12, bounds.Width, bounds.Height));
            DevLogger.Log("AiNoteView",
                $"BringIntoView | tb.Y={bounds.Y:F0} tb.H={bounds.Height:F0} | " +
                $"scrollOff={ContentScroll.Offset.Y:F0} sheetY={SheetRoot?.Bounds.Y:F0}");
        }
        catch (Exception ex)
        {
            DevLogger.Log("AiNoteView", $"BringIntoView failed: {ex.Message}");
        }
    }
}
