using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using ChildNotes.Services;

namespace ChildNotes.Views;

/// <summary>
/// 根据在线状态返回颜色：在线=绿色，离线=红色。
/// </summary>
public class OnlineColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        return value is true ? new SolidColorBrush(Color.Parse("#07C160")) : new SolidColorBrush(Color.Parse("#E74C3C"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// 同步日志状态 → 背景色画笔：success=绿、failed=红、running=蓝（与状态文本同色）。
/// </summary>
public class SyncLogStatusBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        return value is string s ? s switch
        {
            "success" => new SolidColorBrush(Color.Parse("#07C160")),
            "failed" => new SolidColorBrush(Color.Parse("#FA5151")),
            "running" => new SolidColorBrush(Color.Parse("#10AEFF")),
            _ => new SolidColorBrush(Color.Parse("#999999")),
        } : new SolidColorBrush(Color.Parse("#999999"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// 同步日志状态 → 中文文案：success=成功、failed=失败、running=进行中。
/// </summary>
public class SyncLogStatusTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        return value is string s ? s switch
        {
            "success" => "成功",
            "failed" => "失败",
            "running" => "进行中",
            _ => s,
        } : value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public partial class SyncSettingsView : UserControl
{
    /// <summary>静态引用，供 XAML 绑定使用。</summary>
    public static readonly OnlineColorConverter OnlineColorConverter = new();
    public static readonly SyncLogStatusBrushConverter SyncLogStatusBrushConverter = new();
    public static readonly SyncLogStatusTextConverter SyncLogStatusTextConverter = new();

    public SyncSettingsView()
    {
        InitializeComponent();
    }
}
