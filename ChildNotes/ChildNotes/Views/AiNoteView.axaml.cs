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

    // ★ TabBar 高度（从 MainShellView 动态获取，用于扣除测量值中多算的 TabBar 部分）
    private double _tabBarHeight;

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

        // ★ 尝试获取 MainShellView 的 TabBar 高度
        UpdateTabBarHeight();

        DevLogger.Log("AiNoteView", "Attached: listeners added");
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        RemoveHandler(InputElement.GotFocusEvent, OnGotFocus);
        RemoveHandler(InputElement.LostFocusEvent, OnLostFocus);
        KeyboardHeightProvider.HeightChanged -= OnKeyboardHeightChanged;
        DevLogger.Log("AiNoteView", "Detached: listeners removed");
    }

    /// <summary>向上查找 MainShellView 并获取其 TabBar 高度</summary>
    private void UpdateTabBarHeight()
    {
        _tabBarHeight = 0;
        try
        {
            // 弹窗在 MainShellView > Grid.Row=0 > ContentControl 内部
            // 向上查找 MainShellView
            var shell = this.FindAncestorOfType<MainShellView>();
            if (shell is not null)
            {
                // MainShellView 的 TabBar 是 Border.tab-bar（Grid.Row=1）
                var tabBar = shell.GetTemplateChildren()
                    .FirstOrDefault(c => c is Border b && b.Classes.Contains("tab-bar")) as Border;
                if (tabBar?.Bounds.Height > 0)
                {
                    _tabBarHeight = tabBar.Bounds.Height;
                    DevLogger.Log("AiNoteView", $"TabBar height: {_tabBarHeight:F1}lp");
                }
            }
        }
        catch { /* 查找失败时保持为0 */ }
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
    ///
    /// ★ 关键修正：键盘高度测量值是从屏幕底部到可见区域底部的距离，
    ///   这包含了 TabBar 的高度。但弹窗在 TabBar 上方的 overlay 中，
    ///   不需要为 TabBar 预留空间，所以需要扣除 TabBar 高度。
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
            // ★ 扣除 TabBar 高度（测量值包含 TabBar，但弹窗不需要为它预留空间）
            offset = Math.Max(0, kbHeight - _tabBarHeight);
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
            $"ApplyOffset | {reason} | src={offsetSource} | kbH={kbHeight:F1}lp | tabBarH={_tabBarHeight:F1}lp | offset={offset:F1}lp | " +
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
