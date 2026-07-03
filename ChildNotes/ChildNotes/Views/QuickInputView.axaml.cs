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
        UpdateTabBarHeight();
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
            // ★ 不在 GotFocus 时立即上推：此时 KeyboardHeightProvider.CurrentHeight 通常还是 0，
            //   走 fallback 会用屏幕高度 45% 作为偏移（~360lp），与实际键盘高度（~270lp）差距大，
            //   导致视觉抖动：先推 360 再被原生回调覆盖到 270。
            //   原生键盘回调通常在 GotFocus 后 50~150ms 到达，由 OnKeyboardHeightChanged 处理即可。
            //   仅当回调迟迟未到（>250ms）才用 fallback 兜底，避免极少数设备无回调时输入栏不上推。
            DispatcherTimer.RunOnce(() =>
            {
                if (_textBoxFocused && _lastKbOffset == 0)
                {
                    ApplyKeyboardOffset("TextBox focused (fallback after 250ms)");
                }
            }, TimeSpan.FromMilliseconds(250));
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
        if (QuickInputRoot is null) return;
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

        // 纯视觉偏移，不影响布局
        QuickInputRoot.RenderTransform = new TranslateTransform(0, -offset);
        _lastKbOffset = offset;

        DevLogger.Log("QuickInput",
            $"ApplyOffset | {reason} | src={offsetSource} | kbH={kbHeight:F1}lp | tabBarH={_tabBarHeight:F1}lp | offset={offset:F1}lp");
    }

    private void ClearKeyboardOffset(string reason)
    {
        if (QuickInputRoot is null) return;
        QuickInputRoot.RenderTransform = null;
        _lastKbOffset = 0;
        DevLogger.Log("QuickInput", $"ClearOffset | {reason}");
    }
}
