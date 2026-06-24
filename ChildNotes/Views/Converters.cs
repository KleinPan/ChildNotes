using Avalonia.Data.Converters;
using System.Globalization;

namespace ChildNotes.Views;

internal sealed class EqualsConverter : IValueConverter
{
    private readonly object _target;
    public EqualsConverter(object target) => _target = target;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value?.ToString() == _target.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

internal sealed class NotEqualsConverter : IValueConverter
{
    private readonly object _target;
    public NotEqualsConverter(object target) => _target = target;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value?.ToString() != _target.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
