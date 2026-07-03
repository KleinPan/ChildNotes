using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ChildNotes.Infrastructure;
using ChildNotes.ViewModels;

namespace ChildNotes.Views;

/// <summary>
/// 主壳视图 code-behind。
/// 承载首页/喂养/成长/我的 四个 Tab + TabBar + 快捷输入栏 + 功能面板。
///
/// Android 键盘上推策略：
///   当软键盘弹出时，通过给主内容区(Row=0)设置底部 Margin 来减小其有效高度，
///   Grid 布局系统会自动将 Row=1(QuickInput) 上推到键盘上方。
///   这比在 QuickInputView 内部用 RenderTransform 更可靠，
///   因为 RenderTransform 是后布局视觉变换，在 Grid.Row=Auto 的嵌套链中可能被裁剪或忽略。
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
            // 延迟获取 TabBar 高度（布局完成后）
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

    /// <summary>获取 TabBar 高度，用于计算精确的上推偏移量</summary>
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
    /// 核心上推逻辑：通过减小 Row=0 主内容区的有效高度来推动 QuickInput 上移。
    ///
    /// 原理：
    ///   Grid 有 4 行：Row=0(*) / Row=1(Auto) / Row=2(Auto) / Row=3(Auto)
    ///   给 MainContentArea 设置 Margin(0,0,0,kbH-tabBarH) 后，
    ///   Row=0 的可用空间减少 → Grid 重排 → Row=1 自动上移
    ///
    /// 为什么不用 RenderTransform：
    ///   RenderTransform 是"后布局视觉变换"，不参与布局计算。
    ///   QuickInputView 在 Grid.Row=1(Auto) 内部，Panel 只有内容高度(~53lp)，
    ///   TranslateY(-300) 会超出 Panel 边界被裁剪。
    ///   而 Margin 触发真正的 Grid 重排，所有行都正确移动。
    /// </summary>
    private void ApplyKeyboardOffset(string reason)
    {
        if (RootGrid is null) return;

        var kbHeight = KeyboardHeightProvider.CurrentHeight;
        // 扣除 TabBar 高度：键盘弹出时 TabBar 被覆盖，输入栏只需推到键盘上方
        var offset = Math.Max(0, kbHeight - _tabBarHeight);

        // ★ 给 Grid 本身设底部 Margin：将整个 Grid 向上偏移，
        //   所有子元素（Row=0/1/2/3）都会被整体推上去。
        //   注意：不能用子元素 ContentControl.Margin（只在行内部加边距，不影响其他行），
        //   必须操作容器本身才能触发整体重排。
        RootGrid.Margin = new Thickness(0, 0, 0, offset);
        _lastKbOffset = offset;

        DevLogger.Log("Shell",
            $"ApplyOffset | {reason} | kbH={kbHeight:F1}lp | tabBarH={_tabBarHeight:F1}lp | offset={offset:F1}lp | gridH={RootGrid.Bounds.Height:F1}lp");
    }

    private void ClearKeyboardOffset(string reason)
    {
        if (RootGrid is null) return;
        RootGrid.Margin = new Thickness(0);
        _lastKbOffset = 0;
        DevLogger.Log("Shell", $"ClearOffset | {reason}");
    }
}
