using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
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
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        KeyboardHeightProvider.HeightChanged -= OnKeyboardHeightChanged;
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
            ApplyKeyboardOffset("TextBox focused");
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
            // 原生键盘高度可用，直接上推整个键盘高度
            offset = kbHeight;
            offsetSource = "native";
        }
        else if (_textBoxFocused)
        {
            // 原生回调未到但输入框已聚焦：用 fallback 偏移
            var parent = QuickInputRoot.Parent as Layoutable;
            var containerHeight = parent?.Bounds.Height ?? 0;
            offset = containerHeight > 0 ? containerHeight * 0.45 : 350;
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
            $"ApplyOffset | {reason} | src={offsetSource} | kbH={kbHeight:F1}lp | offset={offset:F1}lp");
    }

    private void ClearKeyboardOffset(string reason)
    {
        if (QuickInputRoot is null) return;
        QuickInputRoot.RenderTransform = null;
        _lastKbOffset = 0;
        DevLogger.Log("QuickInput", $"ClearOffset | {reason}");
    }
}
