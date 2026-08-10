using System;
using Avalonia.Controls;
using ChildNotes.Data;

namespace ChildNotes.Views;

/// <summary>
/// 启动 loading 视图：显示品牌插画、文案、育儿小知识 + 持续动画的 indeterminate 进度条。
/// 启动实际加载时间不确定（Session 恢复可能 200ms~2s），用 indeterminate 进度条显示持续动画，
/// 比一次性 0→100 更诚实，且不会出现"卡在某个百分比"的感觉。
/// 育儿知识在构造函数即设置，立即可见不依赖动画。
/// </summary>
public partial class LoadingView : UserControl
{
    public LoadingView()
    {
        InitializeComponent();
        TipText.Text = ParentingTips.GetRandomTip();
    }
}
