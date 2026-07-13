using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace ChildNotes.Views;

/// <summary>
/// 启动 loading 视图：显示 logo 缩放淡入 + 进度条循环动画。
/// 在应用初始化（ServiceProvider 静态构造 + DB 初始化 + 会话恢复）期间作为占位视图，
/// 避免系统启动屏到应用首帧之间的视觉空白。
/// </summary>
public partial class LoadingView : UserControl
{
    private DispatcherTimer? _progressTimer;

    public LoadingView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        // logo 淡入动画（300ms）
        var logoAnim = CreateFadeIn(300, 0);
        _ = logoAnim.RunAsync(LogoBorder);

        // 标题/副标题延迟淡入（logo 动画结束后）
        var titleAnim = CreateFadeIn(150, 300);
        _ = titleAnim.RunAsync(TitleText);
        _ = titleAnim.RunAsync(SubtitleText);

        // 进度条延迟显示 + 循环位移
        var progressFadeIn = CreateFadeIn(200, 400);
        _ = progressFadeIn.RunAsync(ProgressBar);

        _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _progressTimer.Tick += OnProgressTick;
        _progressTimer.Start();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        _progressTimer?.Stop();
        _progressTimer = null;
    }

    /// <summary>进度条循环位移：40px 宽的 fill 在 120px 轨道上往返。</summary>
    private double _progressX;
    private int _progressDir = 1;

    private void OnProgressTick(object? sender, EventArgs e)
    {
        _progressX += _progressDir * 3.0;
        if (_progressX > 80) { _progressX = 80; _progressDir = -1; }
        else if (_progressX < 0) { _progressX = 0; _progressDir = 1; }
        ProgressFill.RenderTransform = new TranslateTransform(_progressX, 0);
    }

    private static Animation CreateFadeIn(int durationMs, int delayMs)
    {
        return new Animation
        {
            Duration = TimeSpan.FromMilliseconds(durationMs),
            Delay = TimeSpan.FromMilliseconds(delayMs),
            Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters = { new Setter(Visual.OpacityProperty, 0d) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters = { new Setter(Visual.OpacityProperty, 1d) }
                }
            }
        };
    }
}
