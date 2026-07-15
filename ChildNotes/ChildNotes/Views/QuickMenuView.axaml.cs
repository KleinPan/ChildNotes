using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace ChildNotes.Views;

/// <summary>
/// 首页底部功能面板 View：点击 + 按钮后在输入栏下方展开图标网格面板。
/// 类似微信的"相册/拍摄/视频通话"功能面板。
/// </summary>
public partial class QuickMenuView : UserControl
{
    // 滑动判定阈值（逻辑像素）：超过此距离才认为是水平滑动
    private const double SwipeThreshold = 40;

    private Point _startPos;
    private bool _tracking;

    public QuickMenuView()
    {
        InitializeComponent();
        Carousel? carousel = null;
        Loaded += (_, _) =>
        {
            carousel = this.FindControl<Carousel>("MenuCarousel");
            if (carousel != null)
            {
                carousel.PointerPressed += OnPointerPressed;
                carousel.PointerReleased += OnPointerReleased;
            }
        };
        Unloaded += (_, _) =>
        {
            if (carousel != null)
            {
                carousel.PointerPressed -= OnPointerPressed;
                carousel.PointerReleased -= OnPointerReleased;
            }
        };
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _startPos = e.GetPosition(this);
        _tracking = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_tracking) return;
        _tracking = false;

        if (sender is not Carousel carousel) return;
        if (carousel.ItemsSource is not System.Collections.ICollection pages) return;
        var count = pages.Count;
        if (count <= 1) return;

        var end = e.GetPosition(this);
        var dx = end.X - _startPos.X;

        if (Math.Abs(dx) < SwipeThreshold) return;

        var current = carousel.SelectedIndex;
        if (dx < 0)
        {
            // 左滑 → 下一页
            if (current < count - 1)
                carousel.SelectedIndex = current + 1;
        }
        else
        {
            // 右滑 → 上一页
            if (current > 0)
                carousel.SelectedIndex = current - 1;
        }
    }
}
