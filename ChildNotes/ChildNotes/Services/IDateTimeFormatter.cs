namespace ChildNotes.Services;

/// <summary>
/// Centralized date/time formatting abstraction.
/// Eliminates duplicated format-string literals across ViewModels and converters.
/// </summary>
/// <remarks>
/// Method list is derived from actual usage found in the codebase:
/// - <see cref="FormatDate"/>: "yyyy-MM-dd" — short date (Baby birth date, milestone date, vaccine record date, etc.)
/// - <see cref="FormatDateTime"/>: "yyyy-MM-dd HH:mm" — date+time (AI analysis labels, vaccine record time)
/// - <see cref="FormatTime"/>: "HH:mm" — time only (record form time fields, record display items)
/// - <see cref="FormatChineseMonthDay"/>: "M月d日" — Chinese month-day (feeding page date header)
/// - <see cref="TryParseDate"/>: parse "yyyy-MM-dd" (DateTextToDateTimeOffsetConverter)
///
/// Methods from the original plan that have NO callers in the codebase are intentionally omitted
/// to avoid dead code: FormatMonthDayTime (MM-dd HH:mm), FormatChineseDate (yyyy年MM月dd日),
/// FormatChineseYearMonth (yyyy年MM月). ParseDate (throwing) is replaced by TryParseDate which
/// matches the only real call site's bool-returning semantics.
/// </remarks>
public interface IDateTimeFormatter
{
    /// <summary>Format: yyyy-MM-dd (e.g., 2024-03-15)</summary>
    string FormatDate(DateTime dateTime);

    /// <summary>Format: yyyy-MM-dd HH:mm (e.g., 2024-03-15 14:30)</summary>
    string FormatDateTime(DateTime dateTime);

    /// <summary>Format: HH:mm (e.g., 14:30) — time only, for record forms and list items</summary>
    string FormatTime(DateTime dateTime);

    /// <summary>Format: M月d日 (e.g., 3月15日) — Chinese month-day, for page date headers</summary>
    string FormatChineseMonthDay(DateTime dateTime);

    /// <summary>Try to parse a "yyyy-MM-dd" string previously produced by <see cref="FormatDate"/>.</summary>
    /// <returns>True if parsing succeeded.</returns>
    bool TryParseDate(string s, out DateTime result);
}
