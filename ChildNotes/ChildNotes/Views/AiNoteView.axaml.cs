using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ChildNotes.Infrastructure;
using ChildNotes.Services;

namespace ChildNotes.Views;

public partial class AiNoteView : UserControl
{
    private const double DefaultMaxHeight = 600;

    private bool _textBoxFocused;
    private double _lastKbOffset;

    // ★ baseline：无键盘时的父容器高度，用于自动补偿 adjustResize 的窗口压缩
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

        // 弹窗打开时记录初始容器高度作为 baseline
        UpdateBaseline();

        DevLogger.Log("AiNoteView", "Attached: listeners added");
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        RemoveHandler(InputElement.GotFocusEvent, OnGotFocus);
        RemoveHandler(InputElement.LostFocusEvent, OnLostFocus);
        KeyboardHeightProvider.HeightChanged -= OnKeyboardHeightChanged;
        DevLogger.Log("AiNoteView", "Detached: listeners removed");
    }

    /// <summary>更新 baseline（在弹窗打开/键盘收回时调用）</summary>
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
        if (!OperatingSystem.IsAndroid()) return;

        // ★ 键盘收回：立即清除偏移让卡片回弹
        if (keyboardHeightLp <= 0 && _lastKbOffset > 0 && IsVisible)
        {
            ClearKeyboardOffset(reason: "keyboard dismissed (native callback)");
            return;
        }

        // 键盘弹出/变化：应用偏移
        if (IsVisible)
        {
            ApplyKeyboardOffset(reason: $"NativeKeyboard height={keyboardHeightLp:F0}lp");
        }
    }

    private void OnGotFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TextBox tb)
        {
            _textBoxFocused = true;
            ApplyKeyboardOffset(reason: $"TextBox focused");
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
                if (!_textBoxFocused && _lastKbOffset > 0)
                {
                    ClearKeyboardOffset(reason: "TextBox lost focus (deferred)");
                }
            }, TimeSpan.FromMilliseconds(200));
        }
    }

    /// <summary>
    /// 核心逻辑：
    ///   1. 用 TranslateTransform.Y 做视觉偏移（不参与布局，避免与 adjustResize 叠加）
    ///   2. 自动补偿 adjustResize 已处理的窗口压缩量（baseline 方案）
    ///
    /// 补偿公式：actualOffset = measuredKbH - (baselineH - currentContainerH)
    /// </summary>
    private void ApplyKeyboardOffset(string reason)
    {
        if (SheetRoot is null) return;
        if (!OperatingSystem.IsAndroid()) return;

        var kbHeight = KeyboardHeightProvider.CurrentHeight;
        var parent = SheetRoot.Parent as Layoutable;
        var containerHeight = parent?.Bounds.Height ?? 0;

        double rawOffset;
        string offsetSource;
        if (kbHeight > 10)
        {
            rawOffset = kbHeight;
            offsetSource = "native";
        }
        else if (_textBoxFocused)
        {
            rawOffset = containerHeight > 0 ? containerHeight * 0.45 : 350;
            offsetSource = "fallback";
        }
        else
        {
            if (_lastKbOffset > 0)
            {
                ClearKeyboardOffset(reason: $"keyboard dismissed ({reason})");
            }
            return;
        }

        // ★ 自动补偿：adjustResize 模式下系统已压缩了窗口，
        //   压缩量 = baseline - currentContainerHeight
        //   这部分不需要我们再推，从原始测量值中扣除
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

        // 安全上限
        var maxOffset = containerHeight > 0 ? containerHeight * 0.92 : 800;
        if (offset > maxOffset)
        {
            offset = maxOffset;
            offsetSource += "(clamped)";
        }

        // ★ TranslateTransform：纯视觉偏移，不触发布局重算
        SheetRoot.RenderTransform = new TranslateTransform(0, -offset);
        SheetRoot.Margin = new Thickness(0);

        if (containerHeight > 0)
        {
            SheetRoot.MaxHeight = Math.Max(280, containerHeight - offset);
        }

        DevLogger.Log("AiNoteView",
            $"ApplyOffset | {reason} | src={offsetSource} | " +
            $"kbH={kbHeight:F1}lp | raw={rawOffset:F1}lp | comp={compensation:F0}lp | offset={offset:F1}lp | " +
            $"containerH={containerHeight:F1}lp | baseline={_baselineContainerHeight:F1}lp | " +
            $"MaxH={SheetRoot.MaxHeight:F0} | sheetY={SheetRoot.Bounds.Y:F0} | " +
            $"sheetBottom={SheetRoot.Bounds.Y + SheetRoot.Bounds.Height:F0}");
        _lastKbOffset = offset;
    }

    /// <summary>清除键盘偏移，恢复默认状态。</summary>
    private void ClearKeyboardOffset(string reason)
    {
        if (SheetRoot is null) return;
        SheetRoot.RenderTransform = null;
        SheetRoot.Margin = new Thickness(0);
        SheetRoot.MaxHeight = DefaultMaxHeight;
        _lastKbOffset = 0;

        // 键盘收回后重新记录基准高度（下次弹出时用于计算补偿）
        UpdateBaseline();

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
