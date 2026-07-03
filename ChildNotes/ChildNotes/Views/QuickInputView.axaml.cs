using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ChildNotes.Infrastructure;

namespace ChildNotes.Views;

/// <summary>
/// 首页底部快捷输入栏 code-behind。
/// 仅承载输入框焦点行为与软键盘上推（仅 Android）。
/// 按需求"点击输入框才聚焦"，不主动调用 Focus()。
/// 键盘弹出时把输入栏整体上推到键盘上方，逻辑对照 RecordSheetView 的成熟方案，
/// 但不调整 MaxHeight（QuickInput 是固定高度输入栏，非底部抽屉）。
/// </summary>
public partial class QuickInputView : UserControl
{
    private bool _textBoxFocused;
    private double _lastKbOffset;

    // ★ TabBar 高度（从 MainShellView 动态获取）
    // QuickInput 位于 MainShell Grid 的 Row=1，TabBar 位于 Row=3。
    // 键盘弹出时 TabBar 本身被键盘覆盖，因此上推量应扣除 TabBar 高度，
    // 否则输入栏会被推得过高（多推一个 TabBar 高度）。
    private double _tabBarHeight;

    public QuickInputView()
    {
        InitializeComponent();
        AddHandler(InputElement.GotFocusEvent, OnGotFocus, RoutingStrategies.Bubble);
        AddHandler(InputElement.LostFocusEvent, OnLostFocus, RoutingStrategies.Bubble);
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        KeyboardHeightProvider.HeightChanged += OnKeyboardHeightChanged;
        // 延迟获取 TabBar 高度：Attached 时 TabBar 可能尚未完成布局，Bounds.Height 可能为 0
        DispatcherTimer.RunOnce(() =>
        {
            UpdateTabBarHeight();
            DevLogger.Log("QuickInput",
                $"Attached | kbH={KeyboardHeightProvider.CurrentHeight:F1}lp | tabBarH={_tabBarHeight:F1}lp | rootH={QuickInputRoot?.Bounds.Height ?? -1:F1}lp");
        }, TimeSpan.FromMilliseconds(300));
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        KeyboardHeightProvider.HeightChanged -= OnKeyboardHeightChanged;
    }

    /// <summary>向上查找 MainShellView 并获取其 TabBar 高度（与 RecordSheetView 同源）</summary>
    private void UpdateTabBarHeight()
    {
        _tabBarHeight = 0;
        try
        {
            var shell = this.FindAncestorOfType<MainShellView>();
            if (shell is not null)
            {
                var tabBar = shell.GetVisualDescendants()
                    .FirstOrDefault(c => c is Border b && b.Classes.Contains("tab-bar")) as Border;
                if (tabBar?.Bounds.Height > 0)
                {
                    _tabBarHeight = tabBar.Bounds.Height;
                    DevLogger.Log("QuickInput", $"TabBar height: {_tabBarHeight:F1}lp");
                }
            }
        }
        catch { /* 查找失败时保持为 0 */ }
    }

    private void OnKeyboardHeightChanged(double keyboardHeightLp)
    {
        if (!OperatingSystem.IsAndroid()) return;

        DevLogger.Log("QuickInput",
            $"KbEvent | height={keyboardHeightLp:F1}lp | focused={_textBoxFocused} | offset={_lastKbOffset:F1}lp");

        // 键盘收回：立即清除偏移
        if (keyboardHeightLp <= 0 && _lastKbOffset > 0)
        {
            ClearKeyboardOffset("keyboard dismissed (native callback)");
            return;
        }

        if (keyboardHeightLp > 0)
        {
            ApplyKeyboardOffset($"NativeKeyboard height={keyboardHeightLp:F0}lp");
        }
    }

    private void OnGotFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TextBox)
        {
            _textBoxFocused = true;
            DevLogger.Log("QuickInput", $"GotFocus | kbH={KeyboardHeightProvider.CurrentHeight:F1}lp | offset={_lastKbOffset:F1}lp");

            // ★ 立即尝试上推一次（此时 kbH 可能已 > 0，若键盘是切换焦点触发的）
            ApplyKeyboardOffset("TextBox focused");

            // 延迟兜底：原生回调通常 50~150ms 到达，若 200ms 后仍无上推，用 fallback 兜底
            DispatcherTimer.RunOnce(() =>
            {
                if (_textBoxFocused && _lastKbOffset == 0)
                {
                    ApplyKeyboardOffset("TextBox focused (fallback after 200ms)");
                }
            }, TimeSpan.FromMilliseconds(200));
        }
    }

    private void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TextBox)
        {
            _textBoxFocused = false;
            // 延迟清除，避免焦点在输入框与发送按钮间切换时误清
            DispatcherTimer.RunOnce(() =>
            {
                if (!_textBoxFocused && _lastKbOffset > 0)
                {
                    ClearKeyboardOffset("TextBox lost focus (deferred)");
                }
            }, TimeSpan.FromMilliseconds(200));
        }
    }

    private void ApplyKeyboardOffset(string reason)
    {
        if (!OperatingSystem.IsAndroid()) return;

        var kbHeight = KeyboardHeightProvider.CurrentHeight;
        double offset;
        string offsetSource;

        if (kbHeight > 10)
        {
            // ★ 原生键盘高度可用：扣除 TabBar 高度（TabBar 被键盘覆盖，输入栏只需上推到键盘上方）
            //   与 RecordSheetView 保持一致
            offset = Math.Max(0, kbHeight - _tabBarHeight);
            offsetSource = "native";
        }
        else if (_textBoxFocused)
        {
            // 原生回调未到但输入框已聚焦：用 fallback 偏移
            // 同样扣除 TabBar 高度，避免上推过多
            var fallbackRaw = _tabBarHeight > 0 ? 270.0 : 320.0;
            offset = Math.Max(0, fallbackRaw - _tabBarHeight);
            offsetSource = "fallback";
        }
        else
        {
            if (_lastKbOffset > 0)
            {
                ClearKeyboardOffset($"keyboard dismissed ({reason})");
            }
            return;
        }

        // ★ 使用 Margin 而非 RenderTransform 实现上推。
        //   原因：在 Android Avalonia 中，UserControl 被嵌套在 ContentControl > Grid.Row=1 时，
        //   RenderTransform（纯视觉后布局变换）可能不正确传播或被布局约束抵消，
        //   导致视觉上没有上移。Margin 会触发真正的布局重排，更可靠。
        //   注意：负的 Top Margin 会让控件在视觉上向上移动，但不影响 Grid.Row=1 的 Auto 高度
        //   （因为 Margin 是控件外部空间，不影响 DesiredSize）。
        //   为防止内容被上方元素遮挡，设置 ZIndex 确保输入栏在最上层。
        Margin = new Thickness(0, -offset, 0, 0);
        ZIndex = 100;
        _lastKbOffset = offset;

        DevLogger.Log("QuickInput",
            $"ApplyOffset | {reason} | src={offsetSource} | kbH={kbHeight:F1}lp | tabBarH={_tabBarHeight:F1}lp | offset={offset:F1}lp | rootH={Bounds.Height:F1}lp");
    }

    private void ClearKeyboardOffset(string reason)
    {
        Margin = new Thickness(0);
        ZIndex = 0;
        _lastKbOffset = 0;
        DevLogger.Log("QuickInput", $"ClearOffset | {reason}");
    }
}
