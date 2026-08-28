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

    /// <summary>确认按钮是否为危险样式（破坏性操作最终确认使用 Danger Button，见 components.md 5.1.6）。</summary>
    public static readonly StyledProperty<bool> IsDangerConfirmProperty =
        AvaloniaProperty.Register<DialogHost, bool>(nameof(IsDangerConfirm));

    /// <summary>可选：底部链接按钮文本（如"升级会员，解锁更多次数"）。为空时不显示链接按钮。</summary>
    public static readonly StyledProperty<string?> LinkTextProperty =
        AvaloniaProperty.Register<DialogHost, string?>(nameof(LinkText));

    /// <summary>可选：底部链接按钮命令（与 <see cref="LinkText"/> 配套，如跳转会员中心）。</summary>
    public static readonly StyledProperty<ICommand?> LinkCommandProperty =
        AvaloniaProperty.Register<DialogHost, ICommand?>(nameof(LinkCommand));

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
        else if (change.Property == IsDangerConfirmProperty)
        {
            UpdateConfirmButtonStyle(change.GetNewValue<bool>());
        }
    }

    /// <summary>
    /// 根据 IsDangerConfirm 切换确认按钮的 Visual Type：
    /// true → btn danger large（破坏性操作最终确认）
    /// false → btn primary large（普通确认）
    /// 规范依据：components.md 5.1.6 Danger Button 业务场景映射（MUST）。
    /// </summary>
    private void UpdateConfirmButtonStyle(bool isDanger)
    {
        if (ConfirmButton == null) return;
        ConfirmButton.Classes.Remove("primary");
        ConfirmButton.Classes.Remove("danger");
        ConfirmButton.Classes.Add(isDanger ? "danger" : "primary");
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
            // 退场用 fast=150ms（design-tokens.md Motion.Duration.Fast）
            int durationFast = GetMotionDuration("Motion.Duration.Fast", 150);
            var maskAnim = CreateFadeAnimation(1, 0, durationFast, new CubicEaseIn());
            var cardAnim = CreateDialogExitAnimation(durationFast, new CubicEaseIn());

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

    /// <summary>
    /// 从应用资源读取 Motion.Duration.* Token 值，读取失败时回退到默认值。
    /// 规范依据：design-tokens.md Motion 章节，动画时长必须引用 Token。
    /// </summary>
    private static int GetMotionDuration(string resourceKey, int fallbackMs)
    {
        if (Application.Current?.TryFindResource(resourceKey, out var value) == true
            && value is double d)
        {
            return (int)d;
        }
        return fallbackMs;
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

    /// <summary>
    /// 确认按钮是否使用 Danger 样式。
    /// 破坏性操作（删除/移除/退出/清空）的最终确认必须设为 true（components.md 5.1.6，MUST）。
    /// </summary>
    public bool IsDangerConfirm
    {
        get => GetValue(IsDangerConfirmProperty);
        set => SetValue(IsDangerConfirmProperty, value);
    }

    /// <summary>可选：底部链接按钮文本。为空（默认）时链接按钮不显示，现有两按钮用法零影响。</summary>
    public string? LinkText
    {
        get => GetValue(LinkTextProperty);
        set => SetValue(LinkTextProperty, value);
    }

    /// <summary>可选：底部链接按钮命令（如"升级会员"跳转）。</summary>
    public ICommand? LinkCommand
    {
        get => GetValue(LinkCommandProperty);
        set => SetValue(LinkCommandProperty, value);
    }
}
