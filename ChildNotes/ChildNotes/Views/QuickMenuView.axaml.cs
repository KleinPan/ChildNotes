using Avalonia.Controls;

namespace ChildNotes.Views;

/// <summary>
/// 首页底部功能面板 View：点击 + 按钮后在输入栏下方展开图标网格面板。
/// 类似微信的"相册/拍摄/视频通话"功能面板。
/// 滑动翻页由 Carousel 内置 IsSwipeEnabled 处理（需配合透明背景使命中测试覆盖空白区域）。
/// </summary>
public partial class QuickMenuView : UserControl
{
    public QuickMenuView()
    {
        InitializeComponent();
    }
}
