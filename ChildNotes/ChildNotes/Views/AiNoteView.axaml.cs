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
        DevLogger.Log("AiNoteView", "Attached: listeners added");
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
    /// 核心逻辑：通过 TranslateTransform.Y 将弹窗推到键盘上方。
    /// 键盘高度由 KeyboardHeightService 精确测量（已扣除导航栏等系统栏）。
    /// </summary>
    private void ApplyKeyboardOffset(string reason)
    {
        if (SheetRoot is null) return;
        if (!OperatingSystem.IsAndroid()) return;

        var kbHeight = KeyboardHeightProvider.CurrentHeight;
        var parent = SheetRoot.Parent as Layoutable;
        var containerHeight = parent?.Bounds.Height ?? 0;

        double offset;
        string offsetSource;
        if (kbHeight > 10)
        {
            offset = kbHeight;
            offsetSource = "native";
        }
        else if (_textBoxFocused)
        {
            offset = containerHeight > 0 ? containerHeight * 0.45 : 350;
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

        // 安全上限
        var maxOffset = containerHeight > 0 ? containerHeight * 0.92 : 800;
        if (offset > maxOffset)
        {
            offset = maxOffset;
            offsetSource += "(clamped)";
        }

        // TranslateTransform：纯视觉偏移
        SheetRoot.RenderTransform = new TranslateTransform(0, -offset);
        SheetRoot.Margin = new Thickness(0);

        if (containerHeight > 0)
        {
            SheetRoot.MaxHeight = Math.Max(280, containerHeight - offset);
        }

        DevLogger.Log("AiNoteView",
            $"ApplyOffset | {reason} | src={offsetSource} | kbH={kbHeight:F1}lp | offset={offset:F1}lp | " +
            $"containerH={containerHeight:F1}lp | MaxH={SheetRoot.MaxHeight:F0} | " +
            $"sheetY={SheetRoot.Bounds.Y:F0} | sheetBottom={SheetRoot.Bounds.Y + SheetRoot.Bounds.Height:F0}");
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
