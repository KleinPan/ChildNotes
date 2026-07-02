using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace ChildNotes.Views;

/// <summary>
/// 导出中状态 → 按钮文案：true=导出中…，false=导出。
/// </summary>
public class ExportingTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        return value is true ? "导出中…" : "导出";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public partial class DeveloperOptionsView : UserControl
{
    /// <summary>静态引用，供 XAML 绑定使用。</summary>
    public static readonly ExportingTextConverter ExportingTextConverter = new();

    public DeveloperOptionsView()
    {
        InitializeComponent();
    }
}
