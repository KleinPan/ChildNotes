using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Styling;
using ChildNotes.Data;

namespace ChildNotes.Views;

/// <summary>
/// 启动 loading 视图：显示育儿小知识 + 进度条循环动画。
/// 系统启动屏已显示应用图标，此处不重复，专注于展示有价值的育儿知识。
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

        // 育儿小知识卡片淡入
        await Task.Delay(100);
        _ = CreateFadeIn(400).RunAsync(TipBorder);

        // 进度条淡入
        await Task.Delay(200);
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
