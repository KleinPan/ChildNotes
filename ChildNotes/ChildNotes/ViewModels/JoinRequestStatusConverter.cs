using Avalonia.Data.Converters;
using ChildNotes.Services;
using System.Globalization;

namespace ChildNotes.ViewModels;

/// <summary>
/// 把家庭加入申请的 Status code（pending/approved/rejected/cancelled）
/// 转为本地化文案，供 FamilyView.axaml 绑定使用。
/// </summary>
public sealed class JoinRequestStatusConverter : IValueConverter
{
    public static readonly JoinRequestStatusConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = value?.ToString() ?? "";
        return status switch
        {
            "pending" => LocaleManager.Instance.GetString("Family_RequestStatusPending", "待审批"),
            "approved" => LocaleManager.Instance.GetString("Family_RequestStatusApproved", "已通过"),
            "rejected" => LocaleManager.Instance.GetString("Family_RequestStatusRejected", "已拒绝"),
            "cancelled" => LocaleManager.Instance.GetString("Family_RequestStatusCancelled", "已取消"),
            _ => status,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
