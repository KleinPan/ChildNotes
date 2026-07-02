using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChildNotes.Controls;

/// <summary>日历热力图模式：按日（7列网格）或按月（4列网格）。</summary>
public enum CalendarMode { Day, Month }

/// <summary>日历热力图单元格数据项。</summary>
public class CalendarCellItem : ObservableObject
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public double Value { get; set; }
    /// <summary>单位（如"次"、"℃"、"ml"等）。</summary>
    public string Unit { get; set; } = "";
    public bool IsToday { get; set; }
    public bool IsFuture { get; set; }
    public bool IsEmpty { get; set; }

    /// <summary>是否有有效数据显示（Value > 0 且非空单元格）。</summary>
    public bool HasValue => !IsEmpty && Value > 0;

    /// <summary>格式化后的值+单位显示文本（如"4次"、"36.5℃"）。</summary>
    public string ValueDisplay => HasValue ? (Value == (int)Value ? $"{(int)Value}{Unit}" : $"{Value:F1}{Unit}") : "";

    // ---- 计算后的样式属性（由控件 UpdateCellStyles 设置） ----
    private IBrush? _cellBackground;
    public IBrush? CellBackground { get => _cellBackground; set => SetProperty(ref _cellBackground, value); }

    private IBrush? _cellBorderBrush;
    public IBrush? CellBorderBrush { get => _cellBorderBrush; set => SetProperty(ref _cellBorderBrush, value); }

    private double _cellOpacity = 1.0;
    public double CellOpacity { get => _cellOpacity; set => SetProperty(ref _cellOpacity, value); }

    private double _cellHeight = 40;
    public double CellHeight { get => _cellHeight; set => SetProperty(ref _cellHeight, value); }

    private IBrush _cellForeground = SolidColorBrush.Parse("#202124");
    public IBrush CellForeground { get => _cellForeground; set => SetProperty(ref _cellForeground, value); }

    private bool _isHighValue;
    public bool IsHighValue { get => _isHighValue; set => SetProperty(ref _isHighValue, value); }
}

/// <summary>
/// 可复用的日历热力图控件，支持 Day/Month 两种模式，
/// 根据数值大小以颜色深浅表示数据密度。
/// 对齐小程序 statistics/index.js 的 buildDayCalendar / buildMonthCalendar + index.wxss 的 .calendar-chart 样式。
/// </summary>
public partial class CalendarHeatMapControl : UserControl
{
    public static readonly StyledProperty<CalendarMode> ModeProperty =
        AvaloniaProperty.Register<CalendarHeatMapControl, CalendarMode>(nameof(Mode), defaultValue: CalendarMode.Day);

    public static readonly StyledProperty<IList<CalendarCellItem>> CellsProperty =
        AvaloniaProperty.Register<CalendarHeatMapControl, IList<CalendarCellItem>>(nameof(Cells));

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<CalendarHeatMapControl, string>(nameof(Title), defaultValue: string.Empty);

    public static readonly StyledProperty<string> SubtitleProperty =
        AvaloniaProperty.Register<CalendarHeatMapControl, string>(nameof(Subtitle), defaultValue: string.Empty);

    public static readonly StyledProperty<string> TypeColorProperty =
        AvaloniaProperty.Register<CalendarHeatMapControl, string>(nameof(TypeColor), defaultValue: "#07C160");

    /// <summary>单元格宽度（由 Mode 自动计算：Day≈38, Month≈计算值）。</summary>
    public static readonly StyledProperty<double> CellWidthProperty =
        AvaloniaProperty.Register<CalendarHeatMapControl, double>(nameof(CellWidth), defaultValue: 38);

    /// <summary>5级透明度（对齐小程序）。</summary>
    private static readonly double[] AlphaLevels = { 0, 0.12, 0.24, 0.42, 0.72 };

    private static readonly IBrush DefaultCellBg = SolidColorBrush.Parse("#F7F8FA");
    private static readonly IBrush DefaultCellBorder = SolidColorBrush.Parse("#EDF0F2");
    private static readonly IBrush TransparentBrush = Brushes.Transparent;

    public CalendarMode Mode { get => GetValue(ModeProperty); set => SetValue(ModeProperty, value); }
    public IList<CalendarCellItem> Cells { get => GetValue(CellsProperty); set => SetValue(CellsProperty, value); }
    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Subtitle { get => GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public string TypeColor { get => GetValue(TypeColorProperty); set => SetValue(TypeColorProperty, value); }
    public double CellWidth { get => GetValue(CellWidthProperty); set => SetValue(CellWidthProperty, value); }

    // ---- x:Name 引用 ----
    private TextBlock? _partTitle;
    private TextBlock? _partSubtitle;
    private TextBlock? _partBadgeText;
    private Grid? _partWeekRow;
    private ItemsControl? _partCells;
    private UniformGrid? _partGrid;

    public CalendarHeatMapControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _partTitle = this.FindControl<TextBlock>("PART_Title");
        _partSubtitle = this.FindControl<TextBlock>("PART_Subtitle");
        _partBadgeText = this.FindControl<TextBlock>("PART_BadgeText");
        _partWeekRow = this.FindControl<Grid>("PART_WeekRow");
        _partCells = this.FindControl<ItemsControl>("PART_Cells");
        _partGrid = this.FindControl<UniformGrid>("PART_Grid");

        RefreshAll();

        // 监听 Cells 集合内容变化（Clear+Add 不会触发 PropertyChanged）
        if (Cells is INotifyCollectionChanged ncc)
            ncc.CollectionChanged += OnCellsCollectionChanged;
    }

    private void OnCellsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // 集合内容变化后重新计算样式
        UpdateCellStyles();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ModeProperty || change.Property == TypeColorProperty ||
            change.Property == TitleProperty || change.Property == SubtitleProperty)
        {
            RefreshAll();
        }
        // Cells 属性引用变化时也刷新 + 重新订阅
        if (change.Property == CellsProperty)
        {
            if (change.OldValue is INotifyCollectionChanged oldNcc)
                oldNcc.CollectionChanged -= OnCellsCollectionChanged;
            if (Cells is INotifyCollectionChanged newNcc)
                newNcc.CollectionChanged += OnCellsCollectionChanged;
            RefreshAll();
        }
    }

    private void RefreshAll()
    {
        if (_partTitle != null) _partTitle.Text = Title;
        if (_partSubtitle != null) _partSubtitle.Text = Subtitle;
        if (_partBadgeText != null) _partBadgeText.Text = "";

        // 星期行仅 Day 模式显示
        if (_partWeekRow != null) _partWeekRow.IsVisible = Mode == CalendarMode.Day;

        ApplyGridColumns();
        UpdateCellStyles();
    }

    /// <summary>根据模式设置 UniformGrid 列数（Day=7, Month=4）。</summary>
    private void ApplyGridColumns()
    {
        var cols = Mode == CalendarMode.Month ? 4 : 7;
        if (_partGrid != null && _partGrid.Columns != cols)
            _partGrid.Columns = cols;
    }

    private void UpdateCellStyles()
    {
        if (Cells == null) return;

        var isDayMode = Mode == CalendarMode.Day;
        var cellHeight = isDayMode ? 40.0 : 48.0;

        var maxValue = Cells.Where(c => !c.IsEmpty && c.Value > 0)
                               .Select(c => c.Value).DefaultIfEmpty(1).Max();
        Color? typeColor = null;
        try { typeColor = string.IsNullOrEmpty(TypeColor) ? null : Color.Parse(TypeColor); } catch { }
        var whiteBrush = Brushes.White;
        var defaultFg = SolidColorBrush.Parse("#202124");

        foreach (var cell in Cells)
        {
            // 高度
            cell.CellHeight = cellHeight;

            if (cell.IsEmpty)
            {
                cell.CellBackground = TransparentBrush;
                cell.CellBorderBrush = TransparentBrush;
                cell.CellOpacity = 1.0; // 空单元格用透明背景而非 opacity
                cell.CellForeground = defaultFg;
                cell.IsHighValue = false;
                continue;
            }

            // 未来日期半透明
            cell.CellOpacity = cell.IsFuture ? 0.45 : 1.0;

            var level = GetAlphaLevel(cell.Value, maxValue);

            if (level > 0 && typeColor.HasValue)
            {
                var bgAlpha = AlphaLevels[Math.Min(level - 1, AlphaLevels.Length - 1)];
                var borderAlpha = Math.Min(bgAlpha + 0.18, 1.0);
                cell.CellBackground = MakeBrush(typeColor.Value, bgAlpha);
                cell.CellBorderBrush = MakeBrush(typeColor.Value, borderAlpha);
                cell.IsHighValue = level >= 4;
            }
            else
            {
                cell.CellBackground = DefaultCellBg;
                cell.CellBorderBrush = DefaultCellBorder;
                cell.IsHighValue = false;
            }

            // 今日额外加绿色边框
            if (cell.IsToday && typeColor.HasValue)
            {
                cell.CellBorderBrush = MakeBrush(typeColor.Value, 0.9);
            }

            // 高值文字变白
            cell.CellForeground = cell.IsHighValue ? whiteBrush : defaultFg;
        }
    }

    private int GetAlphaLevel(double value, double max)
    {
        if (value <= 0 || max <= 0) return 0;
        var ratio = value / max;
        return ratio switch
        {
            > 0.8 => 5,
            > 0.6 => 4,
            > 0.4 => 3,
            > 0.2 => 2,
            > 0   => 1,
            _     => 0
        };
    }

    /// <summary>创建带透明度的 SolidColorBrush（兼容 Avalonia 11+）。</summary>
    private static IBrush MakeBrush(Color baseColor, double alpha)
    {
        var a = (byte)Math.Round(alpha * 255);
        return SolidColorBrush.Parse($"#{a:X2}{baseColor.R:X2}{baseColor.G:X2}{baseColor.B:X2}");
    }
}
