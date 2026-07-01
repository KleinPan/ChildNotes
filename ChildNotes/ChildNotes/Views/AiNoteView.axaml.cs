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
    // 上一次应用的键盘偏移量（用于日志对比和回弹判断）
    private double _lastKbOffset;

    // ★ 自动补偿：记录无键盘时的父容器高度作为基准。
    //   adjustResize 模式下系统会压缩容器，基准 - 当前高度 = 系统已处理的抬升量，
    //   用它补偿 offset 可避免双重抬升导致的间隙。
    private double _baselineContainerHeight;

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

        // 记录初始容器高度作为基准（此时通常无键盘）
        UpdateBaseline();

        DevLogger.Log("AiNoteView", "Attached: GotFocus/LostFocus/KeyboardHeight listeners added");
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        RemoveHandler(InputElement.GotFocusEvent, OnGotFocus);
        RemoveHandler(InputElement.LostFocusEvent, OnLostFocus);
        KeyboardHeightProvider.HeightChanged -= OnKeyboardHeightChanged;
        DevLogger.Log("AiNoteView", "Detached: listeners removed");
    }

    /// <summary>更新容器高度基准（在弹窗打开/键盘收回时调用）</summary>
    private void UpdateBaseline()
    {
        if (SheetRoot is null) return;
        var parent = SheetRoot.Parent as Layoutable;
        if (parent?.Bounds.Height > 0)
        {
            _baselineContainerHeight = parent.Bounds.Height;
            DevLogger.Log("AiNoteView", $"Baseline updated: {_baselineContainerHeight:F1}lp");
        }
    }

    private void OnKeyboardHeightChanged(double keyboardHeightLp)
    {
        // 桌面端不会触发此回调（KeyboardHeightService 只在安卓端注册），但防御性判断
        if (!OperatingSystem.IsAndroid()) return;

        // ★ 键盘收回（height=0）：立即清除偏移让卡片回弹
        //   这是最可靠的回弹触发点，不依赖 TextBox 焦点状态
        //   （LostFocus 有 200ms 延迟，可能晚于键盘收回事件）
        if (keyboardHeightLp <= 0 && _lastKbOffset > 0 && IsVisible)
        {
            ClearKeyboardOffset(reason: "keyboard dismissed (native callback)");
            DevLogger.Log("AiNoteView", $"OnKeyboardHeightChanged | height=0 → cleared offset");
            return;
        }

        // 键盘弹出/变化：应用偏移
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
    /// 核心逻辑：通过设置 SheetRoot 的 Margin.Bottom 将整个抽屉向上推移，
    /// 避开软键盘遮挡。同时限制 MaxHeight 作为安全网。
    ///
    /// ★ 自动补偿机制：
    ///   adjustResize 模式下系统会压缩 Avalonia TopLevel（窗口），
    ///   导致父容器高度从 baseline 缩小。这部分缩小值就是系统已经帮我们做的抬升。
    ///   如果我们再设 Margin.Bottom = 完整键盘高度，就等于双重抬升 → 产生间隙。
    ///
    ///   补偿公式：actualOffset = measuredKbHeight - (baselineH - currentContainerH)
    ///   - 当 adjustResize 未生效：差值为 0，offset 不变
    ///   - 当 adjustResize 生效：差值正好抵消多余抬升，间隙消除
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

        // 使用父容器（Panel）的实际高度作为可用高度基准
        var parent = SheetRoot.Parent as Avalonia.Layout.Layoutable;
        var containerHeight = parent?.Bounds.Height ?? 0;

        // 窗口高度仅用于日志对比
        var topLevel = TopLevel.GetTopLevel(this);
        var windowHeight = topLevel?.ClientSize.Height ?? 0;

        double rawOffset;
        string offsetSource;
        if (kbHeight > 10)
        {
            // 有真实键盘高度：直接用键盘高度作为原始偏移量
            rawOffset = kbHeight;
            offsetSource = "native";
        }
        else if (_textBoxFocused)
        {
            // 已聚焦但还没收到原生回调：用容器高度的 45% 做保守估算
            rawOffset = containerHeight > 0 ? containerHeight * 0.45 : 350;
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

        // ★ 自动补偿：减去 adjustResize 已处理的压缩量
        //   baselineContainerHeight 是无键盘时记录的容器高度
        //   如果当前容器高度 < baseline，说明 adjustResize 已经把内容区上推了
        //   这部分上推量需要从 offset 中扣除，避免双重抬升导致间隙
        double compensation = 0;
        if (_baselineContainerHeight > 0 && containerHeight > 0 && containerHeight < _baselineContainerHeight)
        {
            compensation = _baselineContainerHeight - containerHeight;
        }

        var offset = Math.Max(0, rawOffset - compensation);
        if (compensation > 0)
        {
            offsetSource += $"+auto(-{compensation:F0}lp)";
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
            $"raw={rawOffset:F1}lp | comp={compensation:F0}lp | offset={offset:F1}lp | " +
            $"containerH={containerHeight:F1}lp | baseline={_baselineContainerHeight:F1}lp | winH={windowHeight:F1}lp | " +
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

        // 键盘收回后重新记录基准高度（下次弹出时用于计算补偿）
        UpdateBaseline();

        DevLogger.Log("AiNoteView", $"ClearOffset | {reason} | restored default, baseline reset");
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
