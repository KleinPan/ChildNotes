using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media.Transformation;
using Avalonia.Styling;
using Avalonia.Threading;
using ChildNotes.Services;

namespace ChildNotes.Controls;

/// <summary>
/// 统一确认对话框控件：模态遮罩 + 居中白底卡片 + 标题 / 消息 / 取消&amp;确认按钮。
/// 动画方案：使用 Animation KeyFrame API（C# 代码驱动）。
/// 安卓兼容性：动画完成后显式设置最终属性值，避免 FillMode 在不同平台行为不一致。
/// </summary>
public partial class DialogHost : UserControl
{
    /// <summary>是否显示对话框。</summary>
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<DialogHost, bool>(nameof(IsOpen));

    /// <summary>对话框标题文本。</summary>
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<DialogHost, string?>(nameof(Title));

    /// <summary>取消按钮文本（默认"取消"）。</summary>
    public static readonly StyledProperty<string> CancelTextProperty =
        AvaloniaProperty.Register<DialogHost, string>(nameof(CancelText), defaultValue: "取消");

    /// <summary>确认按钮文本（默认"确认"）。</summary>
    public static readonly StyledProperty<string> ConfirmTextProperty =
        AvaloniaProperty.Register<DialogHost, string>(nameof(ConfirmText), defaultValue: "确认");

    /// <summary>可选：自定义消息内容（支持 Run 内联粗体等复杂文本）。</summary>
    public static readonly StyledProperty<object?> MessageContentProperty =
        AvaloniaProperty.Register<DialogHost, object?>(nameof(MessageContent));

    /// <summary>取消按钮命令。</summary>
    public static readonly StyledProperty<ICommand?> CancelCommandProperty =
        AvaloniaProperty.Register<DialogHost, ICommand?>(nameof(CancelCommand));

    /// <summary>确认按钮命令。</summary>
    public static readonly StyledProperty<ICommand?> ConfirmCommandProperty =
        AvaloniaProperty.Register<DialogHost, ICommand?>(nameof(ConfirmCommand));

    private bool _wasOpen = false;

    public DialogHost()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsOpenProperty)
        {
            var newValue = change.GetNewValue<bool>();
            if (newValue && !_wasOpen)
            {
                _ = OpenDialogAsync();
            }
            else if (!newValue && _wasOpen)
            {
                _ = CloseDialogAsync();
            }
            _wasOpen = newValue;
        }
    }

    /// <summary>
    /// 打开弹窗：显示容器后执行入场动画（遮罩淡入 + 卡片缩放+淡入）。
    /// 动画完成后显式设置最终值，确保跨平台一致。
    /// </summary>
    private async Task OpenDialogAsync()
    {
        if (DialogContainer == null || ModalMask == null || DialogContent == null) return;

        try
        {
            DialogContainer.IsVisible = true;

            // 动画关闭时：直接设置最终状态，跳过动画
            if (!AnimationService.IsEnabled)
            {
                ModalMask.Opacity = 1;
                DialogContent.Opacity = 1;
                DialogContent.RenderTransform = TransformOperations.Parse("none");
                DialogContent.RenderTransformOrigin = RelativePoint.Center;
                return;
            }

            // 确保初始状态
            ModalMask.Opacity = 0;
            DialogContent.Opacity = 0;
            DialogContent.RenderTransform = TransformOperations.Parse("scale(0.9)");
            DialogContent.RenderTransformOrigin = RelativePoint.Center;

            // 等待一帧让布局完成
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

            // 并行执行：遮罩淡入 + 卡片缩放淡入
            var maskAnim = CreateFadeAnimation(0, 1, 250, new CubicEaseOut());
            var cardAnim = CreateDialogEnterAnimation(250, new CubicEaseOut());

            await Task.WhenAll(
                maskAnim.RunAsync(ModalMask),
                cardAnim.RunAsync(DialogContent)
            );

            // ★ 显式设置最终值，避免 FillMode.Forward 在安卓上不生效
            ModalMask.Opacity = 1;
            DialogContent.Opacity = 1;
            DialogContent.RenderTransform = TransformOperations.Parse("none");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DialogHost 打开动画异常: {ex.Message}");
            // 异常时确保弹窗仍可用
            DialogContainer.IsVisible = true;
            ModalMask.Opacity = 1;
            DialogContent.Opacity = 1;
            DialogContent.RenderTransform = TransformOperations.Parse("none");
        }
    }

    /// <summary>
    /// 关闭弹窗：执行退场动画后隐藏容器。
    /// </summary>
    private async Task CloseDialogAsync()
    {
        if (DialogContainer == null || ModalMask == null || DialogContent == null) return;

        try
        {
            // 动画关闭时：直接隐藏
            if (!AnimationService.IsEnabled)
            {
                DialogContainer.IsVisible = false;
                return;
            }

            // 并行执行：遮罩淡出 + 卡片缩小淡出
            var maskAnim = CreateFadeAnimation(1, 0, 200, new CubicEaseIn());
            var cardAnim = CreateDialogExitAnimation(200, new CubicEaseIn());

            await Task.WhenAll(
                maskAnim.RunAsync(ModalMask),
                cardAnim.RunAsync(DialogContent)
            );

            DialogContainer.IsVisible = false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DialogHost 关闭动画异常: {ex.Message}");
            DialogContainer.IsVisible = false;
        }
    }

    /// <summary>创建淡入淡出动画。</summary>
    private static Animation CreateFadeAnimation(double from, double to, int durationMs, Easing easing)
    {
        var anim = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(durationMs),
            Easing = easing,
            FillMode = FillMode.Forward
        };

        var start = new KeyFrame { Cue = new Cue(0.0) };
        start.Setters.Add(new Setter(Visual.OpacityProperty, from));
        anim.Children.Add(start);

        var end = new KeyFrame { Cue = new Cue(1.0) };
        end.Setters.Add(new Setter(Visual.OpacityProperty, to));
        anim.Children.Add(end);

        return anim;
    }

    /// <summary>创建弹窗入场动画（缩放 0.9→1.0 + 淡入 0→1）。</summary>
    private static Animation CreateDialogEnterAnimation(int durationMs, Easing easing)
    {
        var anim = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(durationMs),
            Easing = easing,
            FillMode = FillMode.Forward
        };

        var start = new KeyFrame { Cue = new Cue(0.0) };
        start.Setters.Add(new Setter(Visual.OpacityProperty, 0.0));
        start.Setters.Add(new Setter(Visual.RenderTransformProperty, TransformOperations.Parse("scale(0.9)")));
        anim.Children.Add(start);

        var end = new KeyFrame { Cue = new Cue(1.0) };
        end.Setters.Add(new Setter(Visual.OpacityProperty, 1.0));
        end.Setters.Add(new Setter(Visual.RenderTransformProperty, TransformOperations.Parse("none")));
        anim.Children.Add(end);

        return anim;
    }

    /// <summary>创建弹窗退场动画（缩放 1.0→0.92 + 淡出 1→0）。</summary>
    private static Animation CreateDialogExitAnimation(int durationMs, Easing easing)
    {
        var anim = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(durationMs),
            Easing = easing,
            FillMode = FillMode.Forward
        };

        var start = new KeyFrame { Cue = new Cue(0.0) };
        start.Setters.Add(new Setter(Visual.OpacityProperty, 1.0));
        start.Setters.Add(new Setter(Visual.RenderTransformProperty, TransformOperations.Parse("none")));
        anim.Children.Add(start);

        var end = new KeyFrame { Cue = new Cue(1.0) };
        end.Setters.Add(new Setter(Visual.OpacityProperty, 0.0));
        end.Setters.Add(new Setter(Visual.RenderTransformProperty, TransformOperations.Parse("scale(0.92)")));
        anim.Children.Add(end);

        return anim;
    }

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string CancelText
    {
        get => GetValue(CancelTextProperty);
        set => SetValue(CancelTextProperty, value);
    }

    public string ConfirmText
    {
        get => GetValue(ConfirmTextProperty);
        set => SetValue(ConfirmTextProperty, value);
    }

    public object? MessageContent
    {
        get => GetValue(MessageContentProperty);
        set => SetValue(MessageContentProperty, value);
    }

    public ICommand? CancelCommand
    {
        get => GetValue(CancelCommandProperty);
        set => SetValue(CancelCommandProperty, value);
    }

    public ICommand? ConfirmCommand
    {
        get => GetValue(ConfirmCommandProperty);
        set => SetValue(ConfirmCommandProperty, value);
    }
}
