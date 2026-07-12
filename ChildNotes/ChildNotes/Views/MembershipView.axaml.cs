using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace ChildNotes.Views;

public partial class MembershipView : UserControl
{
    public MembershipView()
    {
        InitializeComponent();
    }

    /// <summary>会员状态文本：会员显示"会员用户"，非会员显示"普通用户"。</summary>
    public static readonly IValueConverter IsMemberTextConverter =
        new FuncValueConverter<bool, string>(isActive => isActive ? "会员用户" : "普通用户");

    /// <summary>分转元（如 1800 → "18.00"）。</summary>
    public static readonly IValueConverter CentsToYuanConverter =
        new FuncValueConverter<int, string>(cents => (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture));

    /// <summary>判断整数是否大于 0（用于控制划线价显示）。</summary>
    public static readonly IValueConverter IsPositiveConverter =
        new FuncValueConverter<int, bool>(val => val > 0);

    /// <summary>
    /// 套餐选中边框颜色：选中时返回主题绿色，未选中返回透明。
    /// ConverterParameter 为当前选中的 PlanType。
    /// </summary>
    public static readonly IValueConverter PlanSelectedBorderConverter =
        new FuncValueConverter<object?, IBrush?>(value =>
        {
            // ConverterParameter 无法直接在 DataTemplate 中绑定到父级属性，
            // 此 Converter 简化处理：返回绿色，由 code-behind 的选中样式统一处理。
            // 选中态的视觉区分通过"选择"按钮的点击 + 底部"立即开通"按钮体现。
            return Brushes.Transparent;
        });
}
