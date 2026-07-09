using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace ChildNotes.Controls;

/// <summary>
/// 物理惯性滚轮日期选择器。与 Avalonia <see cref="DatePicker"/> API 兼容：
/// <see cref="SelectedDate"/> 为 <see cref="DateTimeOffset?"/>，可直接替换 XAML 中的 DatePicker。
/// 复用 <see cref="MomentumWheelList"/> 滚轮引擎，交互与 TimeWheelPicker 一致。
/// </summary>
public class DateWheelPicker : TemplatedControl
{
    // ===== Template Parts =====
    private TextBlock? _displayText;
    private Popup? _popup;
    private ToggleButton? _toggleButton;
    private MomentumWheelList? _yearWheel;
    private MomentumWheelList? _monthWheel;
    private MomentumWheelList? _dayWheel;

    /// <summary>SelectedDate 变化时触发（与 DatePicker.SelectedDateChanged 兼容）。</summary>
    public event EventHandler<DateTimeOffset?>? SelectedDateChanged;

    // ===== 样式属性 =====
    public static readonly StyledProperty<DateTimeOffset?> SelectedDateProperty =
        AvaloniaProperty.Register<DateWheelPicker, DateTimeOffset?>(nameof(SelectedDate),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string> DisplayFormatProperty =
        AvaloniaProperty.Register<DateWheelPicker, string>(nameof(DisplayFormat), "yyyy-MM-dd");

    public static readonly StyledProperty<bool> IsDropdownOpenProperty =
        AvaloniaProperty.Register<DateWheelPicker, bool>(nameof(IsDropdownOpen));

    public static readonly StyledProperty<string?> WatermarkProperty =
        AvaloniaProperty.Register<DateWheelPicker, string?>(nameof(Watermark), "选择日期");

    public static readonly StyledProperty<double> ItemHeightProperty =
        AvaloniaProperty.Register<DateWheelPicker, double>(nameof(ItemHeight), 30);

    public static readonly StyledProperty<double> WheelHeightProperty =
        AvaloniaProperty.Register<DateWheelPicker, double>(nameof(WheelHeight), 130);

    /// <summary>最小年份（默认 1900）。</summary>
    public static readonly StyledProperty<int> MinYearProperty =
        AvaloniaProperty.Register<DateWheelPicker, int>(nameof(MinYear), 1900);

    /// <summary>最大年份（默认当前年份+10）。</summary>
    public static readonly StyledProperty<int> MaxYearProperty =
        AvaloniaProperty.Register<DateWheelPicker, int>(nameof(MaxYear), DateTime.Now.Year + 10);

    static DateWheelPicker()
    {
        SelectedDateProperty.Changed.AddClassHandler<DateWheelPicker, DateTimeOffset?>((c, e) => c.OnSelectedDateChanged(e.NewValue.Value));
        IsDropdownOpenProperty.Changed.AddClassHandler<DateWheelPicker>((c, e) =>
        {
            if (c.IsDropdownOpen)
                c.OnPopupOpening();
        });
    }

    // ===== 公共属性 =====
    public DateTimeOffset? SelectedDate
    {
        get => GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    public string DisplayFormat
    {
        get => GetValue(DisplayFormatProperty);
        set => SetValue(DisplayFormatProperty, value);
    }

    public bool IsDropdownOpen
    {
        get => GetValue(IsDropdownOpenProperty);
        set => SetValue(IsDropdownOpenProperty, value);
    }

    public string? Watermark
    {
        get => GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }

    public double ItemHeight
    {
        get => GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    public double WheelHeight
    {
        get => GetValue(WheelHeightProperty);
        set => SetValue(WheelHeightProperty, value);
    }

    public int MinYear
    {
        get => GetValue(MinYearProperty);
        set => SetValue(MinYearProperty, value);
    }

    public int MaxYear
    {
        get => GetValue(MaxYearProperty);
        set => SetValue(MaxYearProperty, value);
    }

    private bool _suppressWheelEvent;

    // ===== 年份数据源缓存 =====
    private List<string> _yearItems = new();
    private static readonly string[] MonthItems = Enumerable.Range(1, 12).Select(i => i.ToString("00")).ToArray();
    private List<string> _dayItems = new();

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        // 解绑旧 PART
        if (_yearWheel is not null)
            _yearWheel.SelectionChanged -= OnWheelSelectionChanged;
        if (_monthWheel is not null)
            _monthWheel.SelectionChanged -= OnWheelSelectionChanged;
        if (_dayWheel is not null)
            _dayWheel.SelectionChanged -= OnWheelSelectionChanged;

        _displayText = e.NameScope.Find<TextBlock>("PART_DisplayText");
        _popup = e.NameScope.Find<Popup>("PART_Popup");
        _toggleButton = e.NameScope.Find<ToggleButton>("PART_Toggle");
        _yearWheel = e.NameScope.Find<MomentumWheelList>("PART_YearWheel");
        _monthWheel = e.NameScope.Find<MomentumWheelList>("PART_MonthWheel");
        _dayWheel = e.NameScope.Find<MomentumWheelList>("PART_DayWheel");

        // 监听 Popup.Closed 事件（与 TimeWheelPicker 相同的模式）
        if (_popup is not null)
        {
            _popup.Closed -= OnPopupClosedEvent;
            _popup.Closed += OnPopupClosedEvent;
        }

        // 初始化滚轮数据
        if (_yearWheel is not null)
        {
            _yearItems = Enumerable.Range(MinYear, MaxYear - MinYear + 1).Select(y => y.ToString()).ToList();
            _yearWheel.ItemsSource = _yearItems;
            _yearWheel.SelectionChanged += OnWheelSelectionChanged;
            _yearWheel.ItemHeight = ItemHeight;
            _yearWheel.ShouldLoop = false;
        }
        if (_monthWheel is not null)
        {
            _monthWheel.ItemsSource = MonthItems;
            _monthWheel.SelectionChanged += OnWheelSelectionChanged;
            _monthWheel.ItemHeight = ItemHeight;
            _monthWheel.ShouldLoop = true;
        }
        if (_dayWheel is not null)
        {
            _dayItems = Enumerable.Range(1, 31).Select(d => d.ToString("00")).ToList();
            _dayWheel.ItemsSource = _dayItems;
            _dayWheel.SelectionChanged += OnWheelSelectionChanged;
            _dayWheel.ItemHeight = ItemHeight;
            _dayWheel.ShouldLoop = true;
        }

        SyncWheelsFromSelectedDate();
        UpdateDisplayText();
    }

    private void OnPopupOpening()
    {
        // 延迟一帧等待 Popup 完全展开并布局完成，再同步滚轮数据
        // 否则 MomentumWheelList.ArrangeOverride 收到的 finalSize 可能为 0 或不准确，导致数字偏移
        DispatcherTimer.Run(() =>
        {
            SyncWheelsFromSelectedDate();
            _yearWheel?.InvalidateArrange();
            _monthWheel?.InvalidateArrange();
            _dayWheel?.InvalidateArrange();
            return false; // 只执行一次
        }, TimeSpan.FromMilliseconds(1));
    }

    private void OnPopupClosedEvent(object? sender, EventArgs e)
    {
        if (IsDropdownOpen)
            SetCurrentValue(IsDropdownOpenProperty, false);
        if (_toggleButton is not null)
            _toggleButton.IsChecked = false;
    }

    private void OnWheelSelectionChanged(object? sender, EventArgs e)
    {
        if (_suppressWheelEvent) return;
        if (_yearWheel is null || _monthWheel is null || _dayWheel is null) return;

        var yearIdx = _yearWheel.SelectedIndex;
        var monthIdx = _monthWheel.SelectedIndex;
        var dayIdx = _dayWheel.SelectedIndex;

        if (yearIdx < 0 || monthIdx < 0 || dayIdx < 0) return;

        var year = MinYear + yearIdx;
        var month = monthIdx + 1;

        // 联动：月份变化时更新天数
        var daysInMonth = DateTime.DaysInMonth(year, month);
        if (dayIdx >= daysInMonth)
        {
            dayIdx = daysInMonth - 1;
            _suppressWheelEvent = true;
            try { _dayWheel.SelectedIndex = dayIdx; }
            finally { _suppressWheelEvent = false; }
        }

        // 更新天数列表（2月闰年 28/29 天）
        UpdateDayItems(year, month);

        var day = dayIdx + 1;
        var date = new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero);
        SetCurrentValue(SelectedDateProperty, date);
        UpdateDisplayText();
    }

    private void UpdateDayItems(int year, int month)
    {
        if (_dayWheel is null) return;
        var daysInMonth = DateTime.DaysInMonth(year, month);
        if (_dayItems.Count != daysInMonth)
        {
            _dayItems = Enumerable.Range(1, daysInMonth).Select(d => d.ToString("00")).ToList();
            var currentDay = _dayWheel.SelectedIndex;
            _suppressWheelEvent = true;
            try
            {
                _dayWheel.ItemsSource = _dayItems;
                if (currentDay >= 0 && currentDay < daysInMonth)
                    _dayWheel.SelectedIndex = currentDay;
            }
            finally { _suppressWheelEvent = false; }
        }
    }

    private void OnSelectedDateChanged(DateTimeOffset? newValue)
    {
        SyncWheelsFromSelectedDate();
        UpdateDisplayText();
        SelectedDateChanged?.Invoke(this, newValue);
    }

    private void SyncWheelsFromSelectedDate()
    {
        var date = SelectedDate?.DateTime ?? DateTime.Today;
        _suppressWheelEvent = true;
        try
        {
            if (_yearWheel is not null)
                _yearWheel.SelectedIndex = date.Year - MinYear;
            if (_monthWheel is not null)
                _monthWheel.SelectedIndex = date.Month - 1;
            if (_dayWheel is not null)
            {
                UpdateDayItems(date.Year, date.Month);
                _dayWheel.SelectedIndex = date.Day - 1;
            }
        }
        finally { _suppressWheelEvent = false; }
    }

    private void UpdateDisplayText()
    {
        if (_displayText is null) return;
        if (SelectedDate.HasValue)
        {
            var text = SelectedDate.Value.ToString(DisplayFormat);
            _displayText.Text = text;
            _displayText.Foreground = Foreground;
        }
        else
        {
            _displayText.Text = Watermark;
            _displayText.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
        }
    }

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
        return new Size(160, 36);
    }
}
