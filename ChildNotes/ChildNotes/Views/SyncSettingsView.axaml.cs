using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

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

public partial class SyncSettingsView : UserControl
{
    /// <summary>静态引用，供 XAML 绑定使用。</summary>
    public static readonly OnlineColorConverter OnlineColorConverter = new();

    public SyncSettingsView()
    {
        InitializeComponent();
    }
}
