using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ChildNotes.Infrastructure;
using ChildNotes.Shared.Constants;
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
    public static readonly IValueConverter IsWater = new EqualsConverter(RecordType.Water);
    public static readonly IValueConverter IsPump = new EqualsConverter(RecordType.Pump);
    public static readonly IValueConverter IsComplementary = new EqualsConverter(RecordType.Complementary);
    public static readonly IValueConverter IsAbnormal = new EqualsConverter(RecordType.Abnormal);
    public static readonly IValueConverter IsVaccine = new EqualsConverter(RecordType.Vaccine);
    public static readonly IValueConverter NotVaccine = new NotEqualsConverter(RecordType.Vaccine);
    public static readonly IValueConverter IsActivity = new EqualsConverter(RecordType.Activity);

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
    public static readonly IValueConverter IsSupplementType = new EqualsConverter("supplement");
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

    // ===== AI 设置：测试连接结果 =====
    public static readonly IValueConverter TestResultTitleConverter = new BoolToTextConverter("✅ 连接成功", "❌ 连接失败");
    public static readonly IValueConverter TestResultBackgroundConverter = new BoolToBrushConverter("#E8F9EF", "#FFF0F0");
    public static readonly IValueConverter TestResultForegroundConverter = new BoolToBrushConverter("#06AD56", "#E64340");

    // ===== 头像路径 → Bitmap =====
    // BabyBase.Avatar 存储本地文件路径（前端 Baby 继承自 BabyBase），UI 的 Image.Source 需要 IBitmap。
    // 文件不存在或路径为空时返回 null，配合 XAML 中 IsVisible 控制 Image 显隐。
    public static readonly IValueConverter AvatarPathToBitmap = new AvatarPathToBitmapConverter();

    // ===== 程序日志：级别 → 徽章背景色 =====
    // 用于 AppLogView 日志条目的级别徽章 Background 绑定。
    // 修复原实现中 Border 同时挂 lvl-Info/Warn/Error/Debug 四个 class 导致 Debug 灰色覆盖所有的 Bug。
    public static readonly IValueConverter LogLevelToBrush = new LogLevelToBrushConverter();

    // ===== 程序日志：级别 → 徽章前景色（白） =====
    // 所有级别徽章文字统一白色，单独提供以便未来支持暗色徽章时只改一处。
    public static readonly IValueConverter LogLevelToTextBrush = new LogLevelToTextBrushConverter();
}

/// <summary>
/// DevLogger.Level → 徽章背景色转换器。
/// Info=#22A039 绿, Warn=#E6A23C 橙, Error=#F56C6C 红, Debug=#909399 灰。
/// </summary>
internal sealed class LogLevelToBrushConverter : IValueConverter
{
    private static readonly IBrush Info = Brush.Parse("#22A039");
    private static readonly IBrush Warn = Brush.Parse("#E6A23C");
    private static readonly IBrush Error = Brush.Parse("#F56C6C");
    private static readonly IBrush Debug = Brush.Parse("#909399");

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is DevLogger.Level lvl
            ? lvl switch
            {
                DevLogger.Level.Info => Info,
                DevLogger.Level.Warn => Warn,
                DevLogger.Level.Error => Error,
                DevLogger.Level.Debug => Debug,
                _ => Debug
            }
            : Debug;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>DevLogger.Level → 徽章文字色（统一白色）。</summary>
internal sealed class LogLevelToTextBrushConverter : IValueConverter
{
    private static readonly IBrush White = Brushes.White;
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => White;
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 本地头像文件路径 → Bitmap 转换器。
/// 文件不存在或读取失败时返回 null（配合 IsVisible 隐藏 Image，露出占位 emoji）。
/// </summary>
internal sealed class AvatarPathToBitmapConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
            return null;
        if (!System.IO.File.Exists(path))
            return null;
        try
        {
            using var fs = System.IO.File.OpenRead(path);
            return Bitmap.DecodeToWidth(fs, 160);
        }
        catch
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
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
