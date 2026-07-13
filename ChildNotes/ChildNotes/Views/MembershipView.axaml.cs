using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

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
    /// 套餐选中判断（MultiBinding 转换器）：
    /// values[0] = 当前项的 PlanType，values[1] = ViewModel.SelectedPlanType。
    /// 两者相等返回 true（选中），否则 false。
    /// 用于 classes.selected 伪类与 ✓ 图标的 IsVisible。
    /// </summary>
    public static readonly IMultiValueConverter PlanSelectedConverter =
        new FuncMultiValueConverter<object?, bool>(values =>
        {
            var arr = values?.ToArray();
            if (arr is null || arr.Length < 2) return false;
            var currentPlan = arr[0]?.ToString();
            var selectedPlan = arr[1]?.ToString();
            return !string.IsNullOrEmpty(currentPlan) && currentPlan == selectedPlan;
        });

    /// <summary>
    /// 套餐类型转中文名（底部"已选"显示用）。
    /// monthly→月卡，quarterly→季卡，yearly→年卡，空值→"未选择"。
    /// </summary>
    public static readonly IValueConverter PlanTypeToNameConverter =
        new FuncValueConverter<string?, string>(planType => planType switch
        {
            "monthly" => "月卡",
            "quarterly" => "季卡",
            "yearly" => "年卡",
            _ => "未选择",
        });
}
