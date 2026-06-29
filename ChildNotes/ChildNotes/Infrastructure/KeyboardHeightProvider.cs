using Avalonia;

namespace ChildNotes.Infrastructure;

/// <summary>
/// 跨平台软键盘高度提供者。
/// 安卓端由 MainActivity 通过 ViewTreeObserver.OnGlobalLayoutListener 驱动更新，
/// 桌面端/其他平台高度始终为 0。
///
/// 安卓端已用 <c>Resources.DisplayMetrics.Density</c> 将物理像素转换为 Avalonia 逻辑像素，
/// 此处直接存储逻辑像素值，无需二次转换。
/// </summary>
public static class KeyboardHeightProvider
{
    private static double _currentHeight;

    /// <summary>当前键盘高度（逻辑像素），键盘隐藏时为 0。</summary>
    public static double CurrentHeight => _currentHeight;

    /// <summary>键盘高度变化事件（参数为新的逻辑像素高度）。</summary>
    public static event Action<double>? HeightChanged;

    /// <summary>
    /// 由安卓端 MainActivity 回调，传入已转换为 Avalonia 逻辑像素的键盘高度。
    /// </summary>
    internal static void UpdateAndroidHeight(double logicalHeight)
    {
        if (Math.Abs(logicalHeight - _currentHeight) < 0.5) return;

        _currentHeight = logicalHeight;
        DevLogger.Log("Keyboard", $"height updated: {logicalHeight:F1}lp");
        HeightChanged?.Invoke(logicalHeight);
    }
}
