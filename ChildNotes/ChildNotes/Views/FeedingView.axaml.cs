using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ChildNotes.ViewModels;

namespace ChildNotes.Views;

public partial class FeedingView : UserControl
{
    // 滑动判定阈值：松手时位移超过删除按钮宽度的此比例则打开
    private const double SwipeOpenRatio = 0.38;
    // 快速滑动速度阈值（逻辑像素/毫秒）
    private const double SwipeFastVelocity = 0.25;
    // 判定水平/垂直方向的最小位移
    private const double DirectionThreshold = 6;

    private struct SwipeState
    {
        public RecordDisplayItem Item;
        public Border CardBorder;
        public Point StartPos;
        public double StartOffset;
        public Point LastPos;
        public ulong LastMoveTicks;
        public double VelocityX;
        public bool Horizontal;
        public bool Moved;
    }

    private SwipeState? _swipe;

    public FeedingView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        // 监听根 Panel 的指针释放（Bubble），用于"点击空白关闭滑动行"
        if (Content is Panel rootPanel)
        {
            rootPanel.AddHandler(PointerReleasedEvent, OnRootPointerReleased, RoutingStrategies.Bubble);
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        if (Content is Panel rootPanel)
        {
            rootPanel.RemoveHandler(PointerReleasedEvent, OnRootPointerReleased);
        }
        base.OnUnloaded(e);
    }

    /// <summary>
    /// 根 Panel 指针释放：若没有任何滑动进行且点击落在非卡片区域，关闭所有滑动行。
    /// </summary>
    private void OnRootPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_swipe != null) return; // 滑动中，交给卡片 handler 处理
        if (DataContext is not FeedingViewModel vm) return;
        // 检查释放点是否落在某个滑动卡片内
        if (Content is IInputElement inputRoot)
        {
            var hit = inputRoot.InputHitTest(e.GetPosition((Visual)inputRoot));
            bool onCard = false;
            for (var v = hit as Visual; v != null; v = v.GetVisualParent())
            {
                if (v is Border b && b.Tag is RecordDisplayItem)
                {
                    onCard = true;
                    break;
                }
            }
            if (onCard) return;
        }
        if (vm.CloseAllSwipe())
        {
            e.Handled = true;
        }
    }

    private void OnRecordTap(object? sender, TappedEventArgs e)
    {
        if (sender is Border border && border.Tag is RecordDisplayItem item && DataContext is FeedingViewModel vm)
        {
            // 若当前行已展开，点击优先收起；否则进入编辑
            if (item.IsSwipeOpen)
            {
                item.IsSwipeOpen = false;
                item.SwipeOffset = 0;
                return;
            }
            // 其他行展开时，点击此行优先关闭其他行
            if (vm.CloseAllSwipe())
            {
                return;
            }
            vm.EditRecord(item);
        }
    }

    private void OnDeleteTap(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
        if (sender is Border border && border.Tag is RecordDisplayItem item && DataContext is FeedingViewModel vm)
        {
            vm.RequestDelete(item);
        }
    }

    /// <summary>
    /// 卡片指针按下：记录起始状态，准备水平滑动判定。
    /// </summary>
    private void OnCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border || border.Tag is not RecordDisplayItem item) return;
        if (DataContext is not FeedingViewModel vm) return;

        var p = e.GetPosition(this);
        // 按下时若其他行展开，关闭它们（不影响本次滑动开始）
        vm.CloseAllSwipe();

        _swipe = new SwipeState
        {
            Item = item,
            CardBorder = border,
            StartPos = p,
            StartOffset = item.SwipeOffset,
            LastPos = p,
            LastMoveTicks = e.Timestamp,
            VelocityX = 0,
            Horizontal = false,
            Moved = false,
        };
    }

    /// <summary>
    /// 卡片指针移动：水平方向超过阈值后实时更新卡片偏移。
    /// </summary>
    private void OnCardPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_swipe is not { } swipe) return;
        if (sender is not Border border || border != swipe.CardBorder) return;

        var p = e.GetPosition(this);
        var dx = p.X - swipe.StartPos.X;
        var dy = p.Y - swipe.StartPos.Y;

        if (!swipe.Horizontal)
        {
            if (Math.Abs(dx) < DirectionThreshold && Math.Abs(dy) < DirectionThreshold) return;
            if (Math.Abs(dy) > Math.Abs(dx))
            {
                // 垂直方向为主，放弃本次滑动（让 ScrollViewer 滚动）
                _swipe = null;
                return;
            }
            swipe.Horizontal = true;
            _swipe = swipe;
        }

        var now = e.Timestamp;
        var elapsed = Math.Max(1.0, (double)(now - swipe.LastMoveTicks));
        swipe.VelocityX = (p.X - swipe.LastPos.X) / elapsed;
        swipe.LastPos = p;
        swipe.LastMoveTicks = now;

        swipe.Item.SwipeOffset = swipe.StartOffset + dx;
        swipe.Moved = true;
        _swipe = swipe;
        e.Handled = true;
    }

    /// <summary>
    /// 卡片指针释放：根据位移与速度决定打开或收起。
    /// </summary>
    private void OnCardPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_swipe is not { } swipe) return;
        _swipe = null;

        var width = RecordDisplayItem.DeleteActionWidth;
        var current = swipe.Item.SwipeOffset;
        var fastLeft = swipe.VelocityX < -SwipeFastVelocity;
        var fastRight = swipe.VelocityX > SwipeFastVelocity;
        var shouldOpen = fastRight ? false : (fastLeft || current <= -width * SwipeOpenRatio);

        swipe.Item.IsSwipeOpen = shouldOpen;
        swipe.Item.SwipeOffset = shouldOpen ? -width : 0;

        if (swipe.Moved)
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// 指针捕获丢失时平滑收起。
    /// </summary>
    private void OnCardPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_swipe is { } swipe)
        {
            swipe.Item.IsSwipeOpen = false;
            swipe.Item.SwipeOffset = 0;
            _swipe = null;
        }
    }
}
