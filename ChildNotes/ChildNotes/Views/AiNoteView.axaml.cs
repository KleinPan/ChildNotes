using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
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
    ///
    /// 关键修正：使用 SheetRoot 父容器（Panel）的 Bounds.Height 作为可用高度基准，
    /// 而非 TopLevel.ClientSize.Height（后者包含 TabBar 等非内容区域，
    /// 导致基于窗口高度的偏移量计算不准——抽屉无法紧贴键盘上方）。
    /// </summary>
    private void ApplyKeyboardOffset(string reason)
    {
        if (SheetRoot is null)
        {
            DevLogger.Log("AiNoteView", "ApplyOffset SKIP: SheetRoot is null");
            return;
        }

        // 桌面端无软键盘，跳过偏移逻辑
        if (!OperatingSystem.IsAndroid())
        {
            DevLogger.Log("AiNoteView", $"ApplyOffset SKIP: not Android (reason={reason})");
            return;
        }

        var kbHeight = KeyboardHeightProvider.CurrentHeight;

        // ★ 关键：使用父容器（Panel）的实际高度而非窗口高度。
        //   MainShellView 中 AiNoteView 放在 Grid.Row=0（TabBar 上方区域），
        //   所以父容器高度 < ClientSize.Height。用错误的基准会导致偏移不足。
        var parent = SheetRoot.Parent as Avalonia.Layout.Layoutable;
        var containerHeight = parent?.Bounds.Height ?? 0;

        // 窗口高度仅用于 fallback 估算和安全上限
        var topLevel = TopLevel.GetTopLevel(this);
        var windowHeight = topLevel?.ClientSize.Height ?? 0;

        double offset;
        string offsetSource;
        if (kbHeight > 10)
        {
            // 有真实键盘高度：直接用键盘高度作为偏移量
            offset = kbHeight;
            offsetSource = "native";
        }
        else if (_textBoxFocused)
        {
            // 已聚焦但还没收到原生回调：用容器高度的 45% 做保守估算
            offset = containerHeight > 0 ? containerHeight * 0.45 : 350;
            offsetSource = "fallback";
        }
        else
        {
            // 键盘已收回且无焦点：清除偏移，让卡片回弹到原始位置
            if (_lastKbOffset > 0)
            {
                ClearKeyboardOffset(reason: $"keyboard dismissed ({reason})");
            }
            else
            {
                DevLogger.Log("AiNoteView", $"ApplyOffset SKIP: no keyboard & no focus (reason={reason})");
            }
            return;
        }

        // 安全上限：抽屉顶部不超过容器的 8% 位置
        var maxOffset = containerHeight > 0 ? containerHeight * 0.92 : 800;
        if (offset > maxOffset)
        {
            offset = maxOffset;
            offsetSource += "(clamped)";
        }

        // 应用底部 margin 将抽屉上推
        SheetRoot.Margin = new Thickness(0, 0, 0, offset);

        // 限制 MaxHeight：确保抽屉不超出容器可见区
        if (containerHeight > 0)
        {
            SheetRoot.MaxHeight = Math.Max(280, containerHeight - offset);
        }

        DevLogger.Log("AiNoteView",
            $"ApplyOffset | {reason} | src={offsetSource} | kbH={kbHeight:F1}lp | " +
            $"offset={offset:F1}lp | containerH={containerHeight:F1}lp | winH={windowHeight:F1}lp | " +
            $"MaxH={SheetRoot.MaxHeight:F0} | margin={SheetRoot.Margin.Bottom:F0} | " +
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
