using System.Globalization;

namespace ChildNotes.Services;

/// <summary>
/// Default <see cref="IDateTimeFormatter"/> implementation.
/// Formatting uses the current culture (matching the previous inline ToString calls);
/// parsing uses <see cref="CultureInfo.InvariantCulture"/> to match the previous
/// TryParseExact call site in DateTextToDateTimeOffsetConverter.
/// </summary>
public class DateTimeFormatterService : IDateTimeFormatter
{
    private const string DateFormat = "yyyy-MM-dd";
    private const string DateTimeFormat = "yyyy-MM-dd HH:mm";
    private const string TimeFormat = "HH:mm";
    private const string ChineseMonthDayFormat = "M月d日";

    public string FormatDate(DateTime dateTime) => dateTime.ToString(DateFormat);
    public string FormatDateTime(DateTime dateTime) => dateTime.ToString(DateTimeFormat);
    public string FormatTime(DateTime dateTime) => dateTime.ToString(TimeFormat);
    public string FormatChineseMonthDay(DateTime dateTime) => dateTime.ToString(ChineseMonthDayFormat);
    public bool TryParseDate(string s, out DateTime result) =>
        DateTime.TryParseExact(s, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
}
