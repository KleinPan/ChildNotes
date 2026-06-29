using Avalonia.Data.Converters;
using ChildNotes.Infrastructure;
using System.Globalization;

namespace ChildNotes.Views;

/// <summary>
/// 在 <see cref="DateTimeOffset?"/>（DatePicker.SelectedDate）与 "yyyy-MM-dd" 字符串之间双向转换。
/// 仅取日期部分（不含时间），用于 VaccineForm.RecordTimeText 的日期部分。
/// </summary>
public sealed class DateTextToDateTimeOffsetConverter : IValueConverter
{
    public static readonly DateTextToDateTimeOffsetConverter Instance = new();

    /// <summary>ViewModel("yyyy-MM-dd HH:mm" 或 "yyyy-MM-dd") -> DatePicker</summary>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s || string.IsNullOrWhiteSpace(s)) return null;
        // 兼容 "yyyy-MM-dd HH:mm" 与 "yyyy-MM-dd"
        var datePart = s.Length >= 10 ? s.Substring(0, 10) : s;
        return ServiceProvider.Instance.DateTimeFormatter.TryParseDate(datePart, out var dt)
            ? new DateTimeOffset(dt)
            : null;
    }

    /// <summary>DatePicker -> ViewModel("yyyy-MM-dd" 部分)</summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DateTimeOffset dto) return ServiceProvider.Instance.DateTimeFormatter.FormatDate(dto.DateTime);
        if (value is DateTime dt) return ServiceProvider.Instance.DateTimeFormatter.FormatDate(dt);
        return string.Empty;
    }
}
