using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media.Transformation;
using Avalonia.Styling;

// ReSharper disable UnusedMember.Global

namespace ChildNotes.Services;

/// <summary>
/// 动画服务：提供全局开关及 C# 代码动画辅助方法。
/// 大部分 UI 动画通过 XAML Transitions 实现（声明式、高性能）。
/// 此类仅用于无法用 Transitions 的场景（如 IsVisible 从 false→true 时的入场动画）。
/// 所有动画均使用 Avalonia 原生 Animation API（KeyFrame），符合 Avalonia 12 规范。
/// </summary>
public static class AnimationService
{
    #region 时长常量（遵循 avalonia-pro-max/motion 规范）

    /// <summary>微动画（按钮按下反馈）：100ms</summary>
    public const int MicroDuration = 100;

    /// <summary>快速动画（Toast、退场）：150ms</summary>
    public const int FastDuration = 150;

    /// <summary>标准动画（弹窗、抽屉入场）：250ms</summary>
    public const int NormalDuration = 250;

    /// <summary>较慢动画（页面过渡）：350ms</summary>
    public const int SlowDuration = 350;

    #endregion

    #region 全局开关

    /// <summary>
    /// 全局动画开关（默认开启）。
    /// 关闭后仅影响 C# 代码动画（Toast/FAB 等会立即返回，不播放 KeyFrame）；
    /// XAML 声明式 Transitions 不受此开关影响，仍按自身 Duration 播放。
    /// 在设置页面通过 DeveloperOptionsViewModel 控制持久化和实时生效。
    /// </summary>
    public static bool IsEnabled { get; set; } = true;

    #endregion

    #region Toast 动画（用 Animation KeyFrame，因为 Transitions 不会在 IsVisible=false→true 时触发）

    /// <summary>
    /// Toast 提示入场动画：从上方滑入 + 淡入。
    /// 使用 Avalonia Animation KeyFrame API，符合规范。
    /// 动画完成后显式设置最终值，确保跨平台一致。
    /// </summary>
    public static async Task ToastEnter(Control control)
    {
        if (control == null) return;

        // 动画关闭时：直接设置最终状态
        if (!IsEnabled)
        {
            control.Opacity = 1;
            control.RenderTransform = TransformOperations.Parse("none");
            return;
        }

        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(FastDuration),
            Easing = new CubicEaseOut(),
            FillMode = FillMode.Forward
        };

        var startFrame = new KeyFrame { Cue = new Cue(0.0) };
        startFrame.Setters.Add(new Setter(Visual.OpacityProperty, 0.0));
        startFrame.Setters.Add(new Setter(Visual.RenderTransformProperty, TransformOperations.Parse("translateY(-20px)")));
        animation.Children.Add(startFrame);

        var endFrame = new KeyFrame { Cue = new Cue(1.0) };
        endFrame.Setters.Add(new Setter(Visual.OpacityProperty, 1.0));
        endFrame.Setters.Add(new Setter(Visual.RenderTransformProperty, TransformOperations.Parse("none")));
        animation.Children.Add(endFrame);

        await animation.RunAsync(control);

        // ★ 显式设置最终值，避免 FillMode.Forward 在安卓上不生效
        control.Opacity = 1;
        control.RenderTransform = TransformOperations.Parse("none");
    }

    /// <summary>
    /// Toast 提示退场动画：向上滑出 + 淡出。
    /// </summary>
    public static async Task ToastExit(Control control)
    {
        if (control == null) return;

        // 动画关闭时：直接设置最终状态
        if (!IsEnabled)
        {
            control.Opacity = 0;
            return;
        }

        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(FastDuration),
            Easing = new CubicEaseIn(),
            FillMode = FillMode.Forward
        };

        var startFrame = new KeyFrame { Cue = new Cue(0.0) };
        startFrame.Setters.Add(new Setter(Visual.OpacityProperty, 1.0));
        startFrame.Setters.Add(new Setter(Visual.RenderTransformProperty, TransformOperations.Parse("none")));
        animation.Children.Add(startFrame);

        var endFrame = new KeyFrame { Cue = new Cue(1.0) };
        endFrame.Setters.Add(new Setter(Visual.OpacityProperty, 0.0));
        endFrame.Setters.Add(new Setter(Visual.RenderTransformProperty, TransformOperations.Parse("translateY(-10px)")));
        animation.Children.Add(endFrame);

        await animation.RunAsync(control);

        // ★ 显式设置最终值
        control.Opacity = 0;
    }

    #endregion
}
