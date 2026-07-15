using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using ChildNotes.Data;

namespace ChildNotes.Views;

/// <summary>
/// 启动 loading 视图：显示 logo 缩放淡入 + 育儿小知识 + 进度条循环动画。
/// 在应用初始化（ServiceProvider 静态构造 + DB 初始化 + 会话恢复）期间作为占位视图，
/// 避免系统启动屏到应用首帧之间的视觉空白，同时展示育儿知识让等待更有价值。
/// </summary>
public partial class LoadingView : UserControl
{
    public LoadingView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, EventArgs e)
    {
        // 预加载随机育儿小知识
        TipText.Text = ParentingTips.GetRandomTip();

        // logo 缩放 + 淡入（400ms）
        var logoAnim = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(400),
            Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 0d),
                        new Setter(ScaleTransform.ScaleXProperty, 0.6),
                        new Setter(ScaleTransform.ScaleYProperty, 0.6),
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 1d),
                        new Setter(ScaleTransform.ScaleXProperty, 1d),
                        new Setter(ScaleTransform.ScaleYProperty, 1d),
                    }
                }
            }
        };
        _ = logoAnim.RunAsync(LogoBorder);

        // 标题延迟淡入（logo 动画进行中）
        await Task.Delay(200);
        _ = CreateFadeIn(250).RunAsync(TitleText);
        _ = CreateFadeIn(250).RunAsync(SubtitleText);

        // 育儿小知识卡片淡入
        await Task.Delay(100);
        _ = CreateFadeIn(300).RunAsync(TipBorder);

        // 进度条淡入
        await Task.Delay(100);
        _ = CreateFadeIn(200).RunAsync(ProgressBar);
    }

    private static Animation CreateFadeIn(int durationMs)
    {
        return new Animation
        {
            Duration = TimeSpan.FromMilliseconds(durationMs),
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
