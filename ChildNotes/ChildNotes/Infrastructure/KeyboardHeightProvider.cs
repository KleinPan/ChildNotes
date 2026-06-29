using Avalonia;

namespace ChildNotes.Infrastructure;

/// <summary>
/// 跨平台软键盘高度提供者。
/// 安卓端由 MainActivity 通过 ViewTreeObserver.OnGlobalLayoutListener 驱动更新，
/// 桌面端/其他平台高度始终为 0。
///
/// 使用方式：订阅 <see cref="HeightChanged"/> 事件或直接读取 <see cref="CurrentHeight"/>。
/// 高度单位为 Avalonia 逻辑像素（已通过 <see cref="Visual.RenderScaling"/> 转换）。
/// </summary>
public static class KeyboardHeightProvider
{
    private static double _currentHeight;

    /// <summary>当前键盘高度（逻辑像素），键盘隐藏时为 0。</summary>
    public static double CurrentHeight => _currentHeight;

    /// <summary>键盘高度变化事件（参数为新的逻辑像素高度）。</summary>
    public static event Action<double>? HeightChanged;

    /// <summary>由安卓端 MainActivity 回调，传入安卓物理像素高度。</summary>
    internal static void UpdateAndroidHeight(int heightPx)
    {
        // 将安卓物理像素转换为 Avalonia 逻辑像素
        // 使用主窗口的 RenderScaling，若不可用则回退到默认值 2.0（常见安卓 DPI）
        var scaling = GetRenderScaling();
        var logicalHeight = heightPx / scaling;

        if (Math.Abs(logicalHeight - _currentHeight) < 0.5) return; // 忽略微小波动

        _currentHeight = logicalHeight;
        DevLogger.Log("Keyboard", $"height updated: {heightPx}px -> {logicalHeight:F1}lp (scaling={scaling:F2})");
        HeightChanged?.Invoke(logicalHeight);
    }

    private static double GetRenderScaling()
    {
        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is { } lifetime)
            {
                // 尝试从主窗口获取 RenderScaling
                if (lifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    && desktop.MainWindow is not null)
                    return desktop.MainWindow.RenderScaling;
            }
            // 移动端或无法获取时回退到默认值
        }
        catch { }
        return 2.0; // 安卓设备常见的 RenderScaling 默认值
    }
}
