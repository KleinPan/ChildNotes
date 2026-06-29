using Avalonia.Data.Converters;
using System.Globalization;

namespace ChildNotes.Views;

/// <summary>
/// 在 <see cref="TimeSpan?"/>（TimePicker.SelectedTime）与 "HH:mm" 字符串之间双向转换。
/// 用于让 Semi.Avalonia TimePicker 直接绑定到 ViewModel 中的 TimeText 字符串字段。
/// </summary>
public sealed class TimeTextToTimeSpanConverter : IValueConverter
{
    public static readonly TimeTextToTimeSpanConverter Instance = new();

    /// <summary>
    /// ViewModel -> TimePicker：string "HH:mm" 或 "yyyy-MM-dd HH:mm" 转 TimeSpan?
    /// </summary>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s || string.IsNullOrWhiteSpace(s)) return null;
        // 兼容 "yyyy-MM-dd HH:mm" 格式，取时间部分
        if (s.Length >= 16 && s[2] == '-' && s[10] == ' ')
            s = s.Substring(11, 5);
        return TimeSpan.TryParseExact(s, "hh\\:mm", CultureInfo.InvariantCulture, out var ts) ? ts : null;
    }

    /// <summary>
    /// TimePicker -> ViewModel：TimeSpan? 转 string "HH:mm"
    /// </summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is TimeSpan ts) return ts.ToString(@"hh\:mm");
        return string.Empty;
    }
}
