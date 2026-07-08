using System;
using System.Collections;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace ChildNotes.Controls;

/// <summary>
/// 物理惯性滚动 + 智能吸附的滚轮列表（继承自 Panel，无模板，事件 100% 可靠）。
/// 受 iOS UIDatePicker 启发：松手后惯性衰减 + 弹性吸附到最近刻度。
/// 虚拟化渲染：只创建有限数量的容器（可视区+缓冲），根据 offset 动态映射数据。
/// </summary>
public class MomentumWheelList : Panel
{
    // ===== 物理参数 =====
    private const double Friction = 0.945;
    private const double MinVelocityToKeep = 0.35;
    private const double BoundarySpringStiffness = 0.18;
    private const int MaxIdleTicks = 200;

    // ===== 状态字段 =====
    private double _offset;
    private double _velocity;
    private bool _isDragging;
    private bool _isAnimating;
    private Point _lastPosition;
    private readonly List<(DateTime Time, double Y)> _recentMoves = new();
    private int _idleTickCount;

    // ===== 动画状态 =====
    private double _animStartOffset;
    private double _animTargetOffset;
    private DateTime _animStartTime;
    private TimeSpan _animDuration;

    // ===== 数据源缓存（虚拟化渲染核心） =====
    private readonly List<object> _dataSource = new();       // 原始数据源
    private readonly List<ContentControl> _itemContainers = new();  // 固定数量的容器

    // ===== 样式属性 =====
    public static readonly StyledProperty<double> ItemHeightProperty =
        AvaloniaProperty.Register<MomentumWheelList, double>(nameof(ItemHeight), 30);

    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<MomentumWheelList, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<object?> SelectedItemProperty =
        AvaloniaProperty.Register<MomentumWheelList, object?>(nameof(SelectedItem),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<MomentumWheelList, int>(nameof(SelectedIndex), -1,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty =
        AvaloniaProperty.Register<MomentumWheelList, IDataTemplate?>(nameof(ItemTemplate));

    public static readonly StyledProperty<bool> ShouldLoopProperty =
        AvaloniaProperty.Register<MomentumWheelList, bool>(nameof(ShouldLoop), true);

    /// <summary>选中项变化时触发。</summary>
    public event EventHandler? SelectionChanged;

    // 定时器
    private readonly DispatcherTimer _inertiaTimer;
    private readonly DispatcherTimer _animTimer;

    static MomentumWheelList()
    {
        ItemHeightProperty.Changed.AddClassHandler<MomentumWheelList>((c, _) => c.RebuildDataAndContainers());
        ItemsSourceProperty.Changed.AddClassHandler<MomentumWheelList>((c, _) => c.RebuildDataAndContainers());
        SelectedIndexProperty.Changed.AddClassHandler<MomentumWheelList, int>((c, e) => c.OnSelectedIndexChanged(e.NewValue.Value));
        AffectsArrange<MomentumWheelList>(ItemHeightProperty);
    }

    public MomentumWheelList()
    {
        ClipToBounds = true;
        Background = Brushes.Transparent;

        _inertiaTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _inertiaTimer.Tick += OnInertiaTick;

        _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _animTimer.Tick += OnAnimTick;
    }

    // ===== 公共属性 =====
    public double ItemHeight { get => GetValue(ItemHeightProperty); set => SetValue(ItemHeightProperty, value); }
    public IEnumerable? ItemsSource { get => GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public object? SelectedItem { get => GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }
    public int SelectedIndex { get => GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }
    public IDataTemplate? ItemTemplate { get => GetValue(ItemTemplateProperty); set => SetValue(ItemTemplateProperty, value); }
    public bool ShouldLoop { get => GetValue(ShouldLoopProperty); set => SetValue(ShouldLoopProperty, value); }

    /// <summary>原始数据源中的项目数量。</summary>
    private int SourceCount => _dataSource.Count;

    // ============================================================
    //  数据与容器重建（虚拟化）
    // ============================================================

    private void RebuildDataAndContainers()
    {
        // 1. 缓存数据源
        _dataSource.Clear();
        if (ItemsSource is not null)
            foreach (var item in ItemsSource) _dataSource.Add(item);

        // 2. 清除旧子元素
        Children.Clear();
        _itemContainers.Clear();

        // 3. 添加高亮背景 Border（索引 0）
        Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(26, 98, 48, 224)),
            CornerRadius = new CornerRadius(6),
            IsHitTestVisible = false
        });

        // 4. 创建固定数量的容器（可视区约4-5项 + 上下各2项缓冲 = ~9个）
        var containerCount = Math.Max(9, SourceCount);
        for (var i = 0; i < containerCount; i++)
        {
            var container = new ContentControl
            {
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                FontSize = 14,
                Foreground = Brushes.Gray,
                Opacity = 0.35,
                IsHitTestVisible = false
            };
            _itemContainers.Add(container);
            Children.Add(container);
        }

        // 5. 初始化 offset 和选中项
        if (SourceCount > 0 && SelectedIndex >= 0 && SelectedIndex < SourceCount)
            _offset = SelectedIndex * ItemHeight;
        else if (SourceCount > 0)
        {
            _offset = 0;
            SetCurrentValue(SelectedIndexProperty, 0);
        }
        else
        {
            _offset = 0;
        }

        InvalidateArrange();
        UpdateVisuals();
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        RebuildDataAndContainers();
    }

    // ============================================================
    //  核心：ArrangeOverride — 虚拟化布局
    //  根据 _offset 动态决定每个容器显示什么数据、放在哪个 Y 坐标
    // ============================================================

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
            child.Measure(new Size(availableSize.Width, ItemHeight));
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (SourceCount == 0 || _itemContainers.Count == 0) return finalSize;

        var center = finalSize.Height / 2;
        var halfContainerCount = _itemContainers.Count / 2.0;
        var currentLogicalIndex = _offset / ItemHeight;

        // 1. 排列高亮背景（索引 0）
        if (Children[0] is Border highlightBorder)
            highlightBorder.Arrange(new Rect(0, center - ItemHeight / 2, finalSize.Width, ItemHeight));

        // 2. 排列每个数据容器
        for (var i = 0; i < _itemContainers.Count; i++)
        {
            // 容器 i 对应的逻辑位置（相对于当前 offset）
            var logicalPosition = currentLogicalIndex - halfContainerCount + i;

            // 映射到数据源索引
            int dataIndex;
            if (ShouldLoop)
            {
                // 循环模式：模运算
                var rounded = (int)Math.Round(logicalPosition);
                dataIndex = ((rounded % SourceCount) + SourceCount) % SourceCount;
            }
            else
            {
                // 非循环模式：钳制
                dataIndex = (int)Math.Round(logicalPosition);
            }

            // 设置容器内容
            if (dataIndex >= 0 && dataIndex < SourceCount)
            {
                _itemContainers[i].Content = _dataSource[dataIndex];
                _itemContainers[i].ContentTemplate = ItemTemplate;
                _itemContainers[i].IsVisible = true;
            }
            else
            {
                _itemContainers[i].IsVisible = false;
            }

            // 计算 Y 坐标：以 center 为基准，按逻辑位置偏移
            var y = center - ItemHeight / 2 + (logicalPosition - currentLogicalIndex) * ItemHeight;
            _itemContainers[i].Arrange(new Rect(0, y, finalSize.Width, ItemHeight));
        }

        return finalSize;
    }

    // ============================================================
    //  视觉样式更新
    // ============================================================

    private void UpdateVisuals()
    {
        if (_itemContainers.Count == 0 || SourceCount == 0) return;

        var selectedIndex = (int)Math.Round(_offset / ItemHeight);

        for (var i = 0; i < _itemContainers.Count; i++)
        {
            var halfCount = _itemContainers.Count / 2.0;
            var logicalPos = _offset / ItemHeight - halfCount + i;
            var distanceFromCenter = Math.Abs(logicalPos - _offset / ItemHeight);

            // 逻辑距离（考虑循环）
            int logicalDistance;
            if (ShouldLoop)
            {
                var roundedPos = (int)Math.Round(logicalPos);
                var diff = ((roundedPos - selectedIndex) % SourceCount + SourceCount) % SourceCount;
                // 取最短距离（处理循环边界）
                logicalDistance = Math.Min(diff, SourceCount - diff);
            }
            else
            {
                logicalDistance = (int)Math.Round(Math.Abs(logicalPos - selectedIndex));
            }

            // 中心项高亮
            if (Math.Abs(distanceFromCenter) < 0.5)
            {
                _itemContainers[i].Opacity = 1.0;
                _itemContainers[i].FontWeight = FontWeight.Bold;
                _itemContainers[i].FontSize = 15;
                _itemContainers[i].Foreground = new SolidColorBrush(Color.FromRgb(0x62, 0x30, 0xE0));
            }
            else
            {
                var opacity = Math.Max(0.12, 1.0 - distanceFromCenter * 0.18);
                _itemContainers[i].Opacity = opacity;
                _itemContainers[i].FontWeight = FontWeight.Normal;
                _itemContainers[i].FontSize = 13;
                _itemContainers[i].Foreground = Brushes.Gray;
            }
        }
    }

    // ===== 选中项变更 =====
    private void OnSelectedIndexChanged(int newIndex)
    {
        if (newIndex < 0 || newIndex >= SourceCount) return;
        if (!_isDragging && !_isAnimating)
        {
            _offset = newIndex * ItemHeight;
            InvalidateArrange();
            UpdateVisuals();
            SetCurrentValue(SelectedItemProperty, _dataSource[newIndex]);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RaiseSelectionChanged()
    {
        if (SourceCount == 0) return;
        var idx = (int)Math.Round(_offset / ItemHeight);
        if (ShouldLoop)
            idx = ((idx % SourceCount) + SourceCount) % SourceCount;
        else
            idx = Math.Clamp(idx, 0, SourceCount - 1);

        if (idx >= 0 && idx < SourceCount && idx != SelectedIndex)
        {
            SetCurrentValue(SelectedIndexProperty, idx);
            SetCurrentValue(SelectedItemProperty, _dataSource[idx]);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // ============================================================
    //  手势处理（直接在 Panel 上）
    // ============================================================

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEnabled) return;

        StopAnimation();
        _isDragging = true;
        _isAnimating = false;
        _lastPosition = e.GetPosition(this);
        _recentMoves.Clear();
        _velocity = 0;
        _idleTickCount = 0;

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_isDragging) return;

        var current = e.GetPosition(this);
        var delta = current.Y - _lastPosition.Y;
        _lastPosition = current;

        _recentMoves.Add((DateTime.UtcNow, current.Y));
        if (_recentMoves.Count > 5)
            _recentMoves.RemoveAt(0);

        _offset -= delta;
        InvalidateArrange();
        UpdateVisuals();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_isDragging) return;

        _isDragging = false;
        e.Pointer.Capture(null);

        _velocity = ComputeReleaseVelocity();
        _idleTickCount = 0;

        if (Math.Abs(_velocity) > MinVelocityToKeep)
        {
            _isAnimating = true;
            _inertiaTimer.Start();
        }
        else
        {
            StartSnapToNearest();
        }
        e.Handled = true;
    }

    private double ComputeReleaseVelocity()
    {
        if (_recentMoves.Count < 2) return 0;
        var first = _recentMoves[0];
        var last = _recentMoves[^1];
        var dt = (last.Time - first.Time).TotalMilliseconds;
        if (dt < 1) return 0;
        var dy = last.Y - first.Y;
        var pxPerFrame = dy / dt * 16;
        return Math.Clamp(pxPerFrame, -45, 45);
    }

    // ===== 惯性主循环 =====
    private void OnInertiaTick(object? sender, EventArgs e)
    {
        if (_isDragging) { _inertiaTimer.Stop(); return; }

        _idleTickCount++;
        _offset += _velocity;
        _velocity *= Friction;

        if (!ShouldLoop && SourceCount > 0)
        {
            var minOffset = 0;
            var maxOffset = (SourceCount - 1) * ItemHeight;
            if (_offset < minOffset)
            {
                _offset = minOffset - (minOffset - _offset) * (1 - BoundarySpringStiffness);
                _velocity *= -0.4;
            }
            else if (_offset > maxOffset)
            {
                _offset = maxOffset + (_offset - maxOffset) * (1 - BoundarySpringStiffness);
                _velocity *= -0.4;
            }
        }

        InvalidateArrange();
        UpdateVisuals();

        if (Math.Abs(_velocity) < MinVelocityToKeep || _idleTickCount > MaxIdleTicks)
        {
            _inertiaTimer.Stop();
            StartSnapToNearest();
        }
        else
        {
            RaiseSelectionChanged();
        }
    }

    // ===== 吸附动画 =====
    private void StartSnapToNearest()
    {
        if (SourceCount == 0) return;

        var currentIndex = _offset / ItemHeight;
        var targetIndex = ShouldLoop
            ? Math.Round(currentIndex)
            : Math.Clamp(Math.Round(currentIndex), 0, SourceCount - 1);
        var targetOffset = targetIndex * ItemHeight;

        if (Math.Abs(targetOffset - _offset) < 0.1)
        {
            _offset = targetOffset;
            InvalidateArrange();
            UpdateVisuals();
            RaiseSelectionChanged();
            _isAnimating = false;
            return;
        }

        _isAnimating = true;
        _animStartOffset = _offset;
        _animTargetOffset = targetOffset;
        _animStartTime = DateTime.UtcNow;
        _animDuration = TimeSpan.FromMilliseconds(250);
        _animTimer.Start();
    }

    private void OnAnimTick(object? sender, EventArgs e)
    {
        if (!_isAnimating) { _animTimer.Stop(); return; }

        var elapsed = DateTime.UtcNow - _animStartTime;
        if (elapsed >= _animDuration)
        {
            _offset = _animTargetOffset;
            InvalidateArrange();
            UpdateVisuals();
            RaiseSelectionChanged();
            _isAnimating = false;
            _animTimer.Stop();
            return;
        }

        var t = elapsed.TotalMilliseconds / _animDuration.TotalMilliseconds;
        var eased = 1 - Math.Pow(1 - t, 3);
        _offset = _animStartOffset + (_animTargetOffset - _animStartOffset) * eased;
        InvalidateArrange();
        UpdateVisuals();
    }

    private void StopAnimation()
    {
        _inertiaTimer.Stop();
        _animTimer.Stop();
        _isAnimating = false;
    }

    // ===== 鼠标滚轮 =====
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!IsEnabled || SourceCount == 0) return;

        var currentIdx = (int)Math.Round(_offset / ItemHeight);
        var delta = e.Delta.Y > 0 ? -1 : 1;
        var targetIdx = ShouldLoop
            ? currentIdx + delta
            : Math.Clamp(currentIdx + delta, 0, SourceCount - 1);
        var targetOffset = targetIdx * ItemHeight;

        StopAnimation();
        _isAnimating = true;
        _animStartOffset = _offset;
        _animTargetOffset = targetOffset;
        _animStartTime = DateTime.UtcNow;
        _animDuration = TimeSpan.FromMilliseconds(200);
        _animTimer.Start();

        e.Handled = true;
    }

    // ===== 键盘支持 =====
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsEnabled || SourceCount == 0) return;

        switch (e.Key)
        {
            case Key.Up:    StopAnimation(); _offset -= ItemHeight; StartSnapToNearest(); e.Handled = true; break;
            case Key.Down:  StopAnimation(); _offset += ItemHeight; StartSnapToNearest(); e.Handled = true; break;
            case Key.Home:  StopAnimation(); _offset = 0; StartSnapToNearest(); e.Handled = true; break;
            case Key.End:   StopAnimation(); _offset = (SourceCount - 1) * ItemHeight; StartSnapToNearest(); e.Handled = true; break;
        }
    }
}
