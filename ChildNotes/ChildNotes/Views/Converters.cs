using Avalonia.Data.Converters;
using ChildNotes.Models;
using ChildNotes.Services;
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

/// <summary>
/// 集中管理所有 EqualsConverter / NotEqualsConverter 实例，
/// 供各 View 的 XAML 通过 {x:Static v:AppConverters.Xxx} 引用，
/// 消除各 View code-behind 中重复定义的转换器字段。
/// </summary>
public static class AppConverters
{
    // ===== 记录类型 =====
    public static readonly IValueConverter IsFeed = new EqualsConverter(RecordType.Feed);
    public static readonly IValueConverter IsDiaper = new EqualsConverter(RecordType.Diaper);
    public static readonly IValueConverter IsSleep = new EqualsConverter(RecordType.Sleep);
    public static readonly IValueConverter IsTemp = new EqualsConverter(RecordType.Temperature);
    public static readonly IValueConverter IsGrowth = new EqualsConverter(RecordType.Growth);
    public static readonly IValueConverter IsSupplement = new EqualsConverter(RecordType.Supplement);
    public static readonly IValueConverter IsPump = new EqualsConverter(RecordType.Pump);
    public static readonly IValueConverter IsComplementary = new EqualsConverter(RecordType.Complementary);
    public static readonly IValueConverter IsAbnormal = new EqualsConverter(RecordType.Abnormal);
    public static readonly IValueConverter IsVaccine = new EqualsConverter(RecordType.Vaccine);
    public static readonly IValueConverter NotVaccine = new NotEqualsConverter(RecordType.Vaccine);
    public static readonly IValueConverter IsActivity = new EqualsConverter(RecordType.Activity);
    public static readonly IValueConverter IsMaternalFood = new EqualsConverter(RecordType.MaternalFood);

    // ===== 性别 =====
    public static readonly IValueConverter IsBoy = new EqualsConverter("boy");
    public static readonly IValueConverter IsGirl = new EqualsConverter("girl");

    // ===== 喂养类型 =====
    public static readonly IValueConverter IsBottle = new EqualsConverter("bottle");
    public static readonly IValueConverter IsBreast = new EqualsConverter("breast");
    public static readonly IValueConverter IsExpressed = new EqualsConverter("expressed");
    public static readonly IValueConverter NotBreast = new NotEqualsConverter("breast");

    // ===== 尿布类型 =====
    public static readonly IValueConverter IsWet = new EqualsConverter("wet");
    public static readonly IValueConverter IsDirty = new EqualsConverter("dirty");
    public static readonly IValueConverter IsBoth = new EqualsConverter("both");
    public static readonly IValueConverter IsDry = new EqualsConverter("dry");

    // ===== 补给类型 =====
    public static readonly IValueConverter IsMedicine = new EqualsConverter("medicine");
    public static readonly IValueConverter IsNutrition = new EqualsConverter("nutrition");

    // ===== 辅食形态 =====
    public static readonly IValueConverter IsPuree = new EqualsConverter("puree");
    public static readonly IValueConverter IsMashed = new EqualsConverter("mashed");
    public static readonly IValueConverter IsLumpy = new EqualsConverter("lumpy");

    // ===== 辅食反应 =====
    public static readonly IValueConverter IsNoneReaction = new EqualsConverter("none");
    public static readonly IValueConverter IsAllergy = new EqualsConverter("allergy");
    public static readonly IValueConverter IsVomitReaction = new EqualsConverter("vomit");
    public static readonly IValueConverter IsDiarrheaReaction = new EqualsConverter("diarrhea");

    // ===== 异常记录：呕吐类型 =====
    public static readonly IValueConverter IsVomitSpitUp = new EqualsConverter("溢奶");
    public static readonly IValueConverter IsVomitProjectile = new EqualsConverter("喷射");

    // ===== 活动 =====
    public static readonly IValueConverter IsPlay = new EqualsConverter("play");
    public static readonly IValueConverter IsOutdoor = new EqualsConverter("outdoor");
    public static readonly IValueConverter IsExercise = new EqualsConverter("exercise");

    // ===== 疫苗自定义 =====
    public static readonly IValueConverter IsCustomFree = new EqualsConverter("free");
    public static readonly IValueConverter IsCustomPaid = new EqualsConverter("paid");

    // ===== 疫苗状态 =====
    public static readonly IValueConverter IsStatusDone = new EqualsConverter(VaccineDoseStatus.Done);
    public static readonly IValueConverter IsStatusSkipped = new EqualsConverter(VaccineDoseStatus.Skipped);
    public static readonly IValueConverter IsStatusReplaced = new EqualsConverter(VaccineDoseStatus.Replaced);
    public static readonly IValueConverter IsStatusOverdue = new EqualsConverter(VaccineDoseStatus.Overdue);
    public static readonly IValueConverter IsStatusDue = new EqualsConverter(VaccineDoseStatus.Due);
    public static readonly IValueConverter IsStatusSoon = new EqualsConverter(VaccineDoseStatus.Soon);
    public static readonly IValueConverter IsStatusPending = new EqualsConverter(VaccineDoseStatus.Pending);

    // ===== 餐次 =====
    public static readonly IValueConverter IsBreakfast = new EqualsConverter("breakfast");
    public static readonly IValueConverter IsLunch = new EqualsConverter("lunch");
    public static readonly IValueConverter IsDinner = new EqualsConverter("dinner");
    public static readonly IValueConverter IsSnack = new EqualsConverter("snack");

    // ===== 可疑程度 =====
    public static readonly IValueConverter IsNoSuspicion = new EqualsConverter("none");
    public static readonly IValueConverter IsMildSuspicion = new EqualsConverter("mild");
    public static readonly IValueConverter IsHighSuspicion = new EqualsConverter("high");

    // ===== AI 设置：测试连接结果 =====
    public static readonly IValueConverter TestResultTitleConverter = new BoolToTextConverter("✅ 连接成功", "❌ 连接失败");
    public static readonly IValueConverter TestResultBackgroundConverter = new BoolToBrushConverter("#E8F9EF", "#FFF0F0");
    public static readonly IValueConverter TestResultForegroundConverter = new BoolToBrushConverter("#06AD56", "#E64340");
}

/// <summary>布尔值到文本的转换器。</summary>
internal sealed class BoolToTextConverter : IValueConverter
{
    private readonly string _trueText;
    private readonly string _falseText;
    public BoolToTextConverter(string trueText, string falseText) { _trueText = trueText; _falseText = falseText; }
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? (b ? _trueText : _falseText) : _falseText;
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>布尔值到颜色（IBrush）的转换器。</summary>
internal sealed class BoolToBrushConverter : IValueConverter
{
    private readonly Avalonia.Media.IBrush _trueBrush;
    private readonly Avalonia.Media.IBrush _falseBrush;
    public BoolToBrushConverter(string trueHex, string falseHex)
    {
        _trueBrush = Avalonia.Media.Brush.Parse(trueHex);
        _falseBrush = Avalonia.Media.Brush.Parse(falseHex);
    }
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? (b ? _trueBrush : _falseBrush) : _falseBrush;
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
