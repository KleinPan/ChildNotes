using Avalonia.Data.Converters;
using ChildNotes.Services;
using System.Globalization;

namespace ChildNotes.ViewModels;

/// <summary>
/// 把角色 code 转为中文角色名（供 axaml 绑定使用）。
/// </summary>
public sealed class RoleNameConverter : IValueConverter
{
    public static readonly RoleNameConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var code = value?.ToString() ?? "";
        return string.IsNullOrEmpty(code) ? "未选择" : FamilyRoleOptions.GetRoleName(code);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
