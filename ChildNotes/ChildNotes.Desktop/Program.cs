using System;
using Avalonia;
using Avalonia.Media;

#if DEBUG
using Keincheck;
#endif

namespace ChildNotes.Desktop;

sealed class Program
{
    // 注意：在 AppMain 调用前不要使用任何 Avalonia / 第三方 API 或 SynchronizationContext 相关代码，
    // 此时未初始化，可能导致异常。
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia 配置入口（可视化设计器也使用此方法，请勿删除）。
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
            .UseMcpServer()
#endif
            .With(new FontManagerOptions
            {
                DefaultFamilyName = "avares://ChildNotes/Assets/Fonts/wqy-microhei.ttc#WenQuanYi Micro Hei",
                FontFallbacks = new[]
                {
                    new FontFallback { FontFamily = new FontFamily("avares://ChildNotes/Assets/Fonts/wqy-microhei.ttc#WenQuanYi Micro Hei") },
                    new FontFallback { FontFamily = new FontFamily("Microsoft YaHei") },
                    new FontFallback { FontFamily = new FontFamily("sans-serif") }
                }
            })
            .LogToTrace();
}
