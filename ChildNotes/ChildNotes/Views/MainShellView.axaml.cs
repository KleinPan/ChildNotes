using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ChildNotes.Infrastructure;
using ChildNotes.ViewModels;

namespace ChildNotes.Views;

/// <summary>
/// 主壳视图 code-behind。
/// 承载首页/喂养/成长/我的 四个 Tab + TabBar + 快捷输入栏 + 功能面板。
///
/// Android 键盘上推策略：
///   当软键盘弹出时，对 RootGrid（MainShellView 的直接子元素）设 RenderTransform，
///   将整个 Grid 视觉上移，输入栏自然跟随到键盘上方。
///
///   为什么对 RootGrid 设 RenderTransform 能成功（而对 QuickInputView 不行）：
///     - RootGrid 是 UserControl 的直接子元素，拥有全窗口空间，不会被父容器裁剪
///     - QuickInputView 嵌套在 ContentControl > Grid.Row=1(Auto) 内部，
///       父容器只有 ~53lp 高度，内部 Transform 超出边界被裁剪
/// </summary>
public partial class MainShellView : UserControl
{
    private double _lastKbOffset;
    private double _tabBarHeight;

    public MainShellView()
    {
        InitializeComponent();
        DevLogger.Log("Shell", "MainShellView ctor");
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        DevLogger.Log("Shell", $"MainShellView.OnDataContextChanged: type={DataContext?.GetType().Name}");
        if (DataContext is MainShellViewModel vm)
        {
            vm.ActivateHome();
            DevLogger.Log("Shell", "MainShellView.OnDataContextChanged: ActivateHome done");
        }
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        DevLogger.Log("Shell", "MainShellView.OnAttachedToVisualTree");

        if (OperatingSystem.IsAndroid())
        {
            KeyboardHeightProvider.HeightChanged += OnKeyboardHeightChanged;
            DispatcherTimer.RunOnce(FetchTabBarHeight, TimeSpan.FromMilliseconds(500));
        }
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (OperatingSystem.IsAndroid())
        {
            KeyboardHeightProvider.HeightChanged -= OnKeyboardHeightChanged;
        }
    }

    private void FetchTabBarHeight()
    {
        _tabBarHeight = 0;
        if (TabBarBorder?.Bounds.Height > 0)
        {
            _tabBarHeight = TabBarBorder.Bounds.Height;
            DevLogger.Log("Shell", $"TabBar height: {_tabBarHeight:F1}lp");
        }
    }

    private void OnKeyboardHeightChanged(double keyboardHeightLp)
    {
        if (!OperatingSystem.IsAndroid()) return;

        DevLogger.Log("Shell",
            $"KbEvent | kbH={keyboardHeightLp:F1}lp | quickInputVis={QuickInputContainer?.IsVisible} | offset={_lastKbOffset:F1}lp");

        if (keyboardHeightLp <= 0 && _lastKbOffset > 0)
        {
            ClearKeyboardOffset("keyboard dismissed");
            return;
        }

        if (keyboardHeightLp > 0 && QuickInputContainer?.IsVisible == true)
        {
            ApplyKeyboardOffset($"NativeKeyboard height={keyboardHeightLp:F0}lp");
        }
    }

    /// <summary>
    /// 对 RootGrid（UserControl 直接子元素）设 RenderTransform 上推整个界面。
    ///
    /// RootGrid 与 QuickInputView 的关键区别：
    ///   QuickInputView: ContentControl → Grid.Row=1(Auto) → UserControl → ... → 父容器仅 53lp 高 → Transform 被裁剪 ❌
    ///   RootGrid:       MainShellView(UserControl, 全屏高) → Grid(直接子元素) → 充足空间不裁剪 ✅
    /// </summary>
    private void ApplyKeyboardOffset(string reason)
    {
        if (RootGrid is null) return;

        var kbHeight = KeyboardHeightProvider.CurrentHeight;
        var offset = Math.Max(0, kbHeight - _tabBarHeight);

        RootGrid.RenderTransform = new TranslateTransform(0, -offset);
        _lastKbOffset = offset;

        DevLogger.Log("Shell",
            $"ApplyOffset | {reason} | kbH={kbHeight:F1}lp | tabBarH={_tabBarHeight:F1}lp | offset={offset:F1}lp | gridH={RootGrid.Bounds.Height:F1}lp");
    }

    private void ClearKeyboardOffset(string reason)
    {
        if (RootGrid is null) return;
        RootGrid.RenderTransform = null;
        _lastKbOffset = 0;
        DevLogger.Log("Shell", $"ClearOffset | {reason}");
    }
}
