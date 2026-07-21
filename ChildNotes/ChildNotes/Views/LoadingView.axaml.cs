using System;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Styling;
using ChildNotes.Data;

namespace ChildNotes.Views;

/// <summary>
/// 启动 loading 视图：显示品牌插画、文案、育儿小知识 + 一次性进度条。
/// 进度条从 0 走到 100 后停止，不循环，与 App.axaml.cs 中最小显示时长 1.5s 对齐。
/// 育儿知识在构造函数即设置，立即可见不依赖动画。
/// </summary>
public partial class LoadingView : UserControl
{
    public LoadingView()
    {
        InitializeComponent();
        TipText.Text = ParentingTips.GetRandomTip();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // 进度条 1.5 秒内从 0 走到 100，到达终点后保持（FillMode.Forward），不循环。
        // 该时长与 App.axaml.cs 中 LoadingView 最小显示时长一致。
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(1500),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters = { new Setter(ProgressBar.ValueProperty, 0d) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters = { new Setter(ProgressBar.ValueProperty, 100d) }
                }
            }
        };
        animation.RunAsync(ProgressBar);
    }
}
