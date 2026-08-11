using Avalonia;
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
        return value is true ? SyncBrushResolver.ResolveBrush("BrandPrimaryBrush") : SyncBrushResolver.ResolveBrush("SemanticErrorBrush");
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
            "success" => SyncBrushResolver.ResolveBrush("BrandPrimaryBrush"),
            "failed" => SyncBrushResolver.ResolveBrush("SemanticErrorBrush"),
            "running" => SyncBrushResolver.ResolveBrush("GrowthBlueBrush"),
            _ => SyncBrushResolver.ResolveBrush("TextPlaceholderBrush"),
        } : SyncBrushResolver.ResolveBrush("TextPlaceholderBrush");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// 从 Application 全局资源中解析 Brush（DesignTokens 已在 App.axaml 合并）。
/// Converter 无法直接用 StaticResource，通过此方法在运行时解析。
/// </summary>
internal static class SyncBrushResolver
{
    public static IBrush? ResolveBrush(string key)
    {
        if (Application.Current?.Resources.TryGetResource(key, null, out var v) == true && v is IBrush b)
            return b;
        return null;
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
