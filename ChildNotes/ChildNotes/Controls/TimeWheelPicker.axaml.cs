using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace ChildNotes.Controls;

/// <summary>
/// 物理惯性滚轮 + 智能吸附的时间选择器。
/// 与 Avalonia <see cref="TimePicker"/> API 兼容：<see cref="SelectedTime"/> 为 <see cref="TimeSpan?"/>，
/// 可直接替换 XAML 中的 TimePicker（绑定路径与转换器无需改动）。
/// 交互：点击展开 Popup → 拖动/滚轮/键盘调整时分秒 → Popup 外部点击关闭。
/// </summary>
public class TimeWheelPicker : TemplatedControl
{
    // ===== Template Parts =====
    private TextBlock? _displayText;
    private Popup? _popup;
    private ToggleButton? _toggleButton;
    private MomentumWheelList? _hourWheel;
    private MomentumWheelList? _minuteWheel;
    private MomentumWheelList? _secondWheel;

    // ===== 样式属性 =====
    public static readonly StyledProperty<TimeSpan?> SelectedTimeProperty =
        AvaloniaProperty.Register<TimeWheelPicker, TimeSpan?>(nameof(SelectedTime),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string> DisplayFormatProperty =
        AvaloniaProperty.Register<TimeWheelPicker, string>(nameof(DisplayFormat), @"hh\:mm");

    public static readonly StyledProperty<bool> ShowSecondsProperty =
        AvaloniaProperty.Register<TimeWheelPicker, bool>(nameof(ShowSeconds));

    public static readonly StyledProperty<bool> IsDropdownOpenProperty =
        AvaloniaProperty.Register<TimeWheelPicker, bool>(nameof(IsDropdownOpen));

    public static readonly StyledProperty<string?> WatermarkProperty =
        AvaloniaProperty.Register<TimeWheelPicker, string?>(nameof(Watermark), "选择时间");

    public static readonly StyledProperty<double> ItemHeightProperty =
        AvaloniaProperty.Register<TimeWheelPicker, double>(nameof(ItemHeight), 30);

    public static readonly StyledProperty<double> WheelHeightProperty =
        AvaloniaProperty.Register<TimeWheelPicker, double>(nameof(WheelHeight), 130);

    static TimeWheelPicker()
    {
        SelectedTimeProperty.Changed.AddClassHandler<TimeWheelPicker, TimeSpan?>((c, e) => c.OnSelectedTimeChanged(e.NewValue.Value));
        ShowSecondsProperty.Changed.AddClassHandler<TimeWheelPicker>((c, e) => c.UpdateSecondsVisibility());
        IsDropdownOpenProperty.Changed.AddClassHandler<TimeWheelPicker>((c, e) =>
        {
            if (c.IsDropdownOpen)
                c.OnPopupOpening();
            else
                c.OnPopupClosed();
        });
    }

    // ===== 公共属性 =====
    /// <summary>选中的时间。与 Avalonia TimePicker 兼容。</summary>
    public TimeSpan? SelectedTime
    {
        get => GetValue(SelectedTimeProperty);
        set => SetValue(SelectedTimeProperty, value);
    }

    /// <summary>显示格式（TimeSpan 格式字符串），默认 "HH:mm"。</summary>
    public string DisplayFormat
    {
        get => GetValue(DisplayFormatProperty);
        set => SetValue(DisplayFormatProperty, value);
    }

    /// <summary>是否显示秒滚轮。默认 false（仅时分）。</summary>
    public bool ShowSeconds
    {
        get => GetValue(ShowSecondsProperty);
        set => SetValue(ShowSecondsProperty, value);
    }

    /// <summary>Popup 是否展开。</summary>
    public bool IsDropdownOpen
    {
        get => GetValue(IsDropdownOpenProperty);
        set => SetValue(IsDropdownOpenProperty, value);
    }

    /// <summary>占位提示文本。</summary>
    public string? Watermark
    {
        get => GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }

    /// <summary>滚轮单项高度。</summary>
    public double ItemHeight
    {
        get => GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    /// <summary>滚轮可视区高度（建议为 ItemHeight 的 5 倍，显示 5 项）。</summary>
    public double WheelHeight
    {
        get => GetValue(WheelHeightProperty);
        set => SetValue(WheelHeightProperty, value);
    }

    private bool _suppressWheelEvent;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        // 先解绑旧 PART
        if (_hourWheel is not null)
            _hourWheel.SelectionChanged -= OnWheelSelectionChanged;
        if (_minuteWheel is not null)
            _minuteWheel.SelectionChanged -= OnWheelSelectionChanged;
        if (_secondWheel is not null)
            _secondWheel.SelectionChanged -= OnWheelSelectionChanged;

        _displayText = e.NameScope.Find<TextBlock>("PART_DisplayText");
        _popup = e.NameScope.Find<Popup>("PART_Popup");
        _toggleButton = e.NameScope.Find<ToggleButton>("PART_Toggle");
        _hourWheel = e.NameScope.Find<MomentumWheelList>("PART_HourWheel");
        _minuteWheel = e.NameScope.Find<MomentumWheelList>("PART_MinuteWheel");
        _secondWheel = e.NameScope.Find<MomentumWheelList>("PART_SecondWheel");

        // 监听 Popup 的 Closed 事件，处理 LightDismiss 关闭时的状态同步
        if (_popup is not null)
        {
            _popup.Closed -= OnPopupClosedEvent;
            _popup.Closed += OnPopupClosedEvent;
        }

        if (_hourWheel is not null)
        {
            _hourWheel.ItemsSource = Enumerable.Range(0, 24).Select(i => i.ToString("00"));
            _hourWheel.SelectionChanged += OnWheelSelectionChanged;
            _hourWheel.ItemHeight = ItemHeight;
        }
        if (_minuteWheel is not null)
        {
            _minuteWheel.ItemsSource = Enumerable.Range(0, 60).Select(i => i.ToString("00"));
            _minuteWheel.SelectionChanged += OnWheelSelectionChanged;
            _minuteWheel.ItemHeight = ItemHeight;
        }
        if (_secondWheel is not null)
        {
            _secondWheel.ItemsSource = Enumerable.Range(0, 60).Select(i => i.ToString("00"));
            _secondWheel.SelectionChanged += OnWheelSelectionChanged;
            _secondWheel.ItemHeight = ItemHeight;
        }

        UpdateSecondsVisibility();
        SyncWheelsFromSelectedTime();
        UpdateDisplayText();
    }

    /// <summary>Popup 打开时强制刷新滚轮数据和选中状态。</summary>
    private void OnPopupOpening()
    {
        SyncWheelsFromSelectedTime();
        // 强制触发 MomentumWheelList 的数据重建（修复关闭再打开不显示的问题）
        _hourWheel?.InvalidateArrange();
        _minuteWheel?.InvalidateArrange();
        _secondWheel?.InvalidateArrange();
    }

    /// <summary>Popup 关闭事件处理：同步 ToggleButton 状态和清除焦点。</summary>
    private void OnPopupClosedEvent(object? sender, EventArgs e)
    {
        // LightDismiss 关闭 Popup 后，需要同步 IsDropdownOpen 和 ToggleButton 状态
        if (IsDropdownOpen)
            SetCurrentValue(IsDropdownOpenProperty, false);
        if (_toggleButton is not null)
        {
            _toggleButton.IsChecked = false;
        }
    }

    /// <summary>Popup 关闭时清除 ToggleButton 焦点，避免状态不同步导致双击。</summary>
    private void OnPopupClosed()
    {
        // 保留此方法用于 IsDropdownOpen 属性变化时的处理（备用）
    }

    private void OnWheelSelectionChanged(object? sender, EventArgs e)
    {
        if (_suppressWheelEvent) return;
        if (_hourWheel is null || _minuteWheel is null) return;

        var h = _hourWheel.SelectedIndex;
        var m = _minuteWheel.SelectedIndex;
        var s = ShowSeconds && _secondWheel is not null ? _secondWheel.SelectedIndex : 0;

        if (h < 0 || m < 0) return;
        var ts = new TimeSpan(h, m, s);
        SetCurrentValue(SelectedTimeProperty, ts);
        UpdateDisplayText();
    }

    private void OnSelectedTimeChanged(TimeSpan? newValue)
    {
        SyncWheelsFromSelectedTime();
        UpdateDisplayText();
    }

    private void SyncWheelsFromSelectedTime()
    {
        var ts = SelectedTime ?? TimeSpan.Zero;
        _suppressWheelEvent = true;
        try
        {
            if (_hourWheel is not null)
                _hourWheel.SelectedIndex = ts.Hours;
            if (_minuteWheel is not null)
                _minuteWheel.SelectedIndex = ts.Minutes;
            if (_secondWheel is not null)
                _secondWheel.SelectedIndex = ts.Seconds;
        }
        finally
        {
            _suppressWheelEvent = false;
        }
    }

    private void UpdateDisplayText()
    {
        if (_displayText is null) return;
        if (SelectedTime.HasValue)
        {
            // TimeSpan 格式说明符为小写 hh（大写 HH 在 TimeSpan 中无效，会抛 FormatException）
            var text = SelectedTime.Value.ToString();
            try { text = SelectedTime.Value.ToString(DisplayFormat); }
            catch (FormatException) { text = SelectedTime.Value.ToString(@"hh\:mm"); }
            _displayText.Text = text;
            _displayText.Foreground = Foreground;
        }
        else
        {
            _displayText.Text = Watermark;
            _displayText.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
        }
    }

    private void UpdateSecondsVisibility()
    {
        if (_secondWheel is not null)
            _secondWheel.IsVisible = ShowSeconds;
    }

    // ===== 键盘 Esc 关闭 =====
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && IsDropdownOpen)
        {
            SetCurrentValue(IsDropdownOpenProperty, false);
            e.Handled = true;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(120, 36);
    }
}
