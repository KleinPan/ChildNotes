using Foundation;
using UIKit;
using Avalonia;
using Avalonia.Controls;
using Avalonia.iOS;
using Avalonia.Media;

namespace ChildNotes.iOS;

// iOS 应用 UIApplicationDelegate：负责启动 UI 并响应 iOS 应用生命周期事件。
[Register("AppDelegate")]
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
public partial class AppDelegate : AvaloniaAppDelegate<App>
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .With(new FontManagerOptions
            {
                DefaultFamilyName = "avares://ChildNotes/Assets/Fonts/wqy-microhei.ttc#WenQuanYi Micro Hei",
                FontFallbacks = new[]
                {
                    new FontFallback { FontFamily = new FontFamily("avares://ChildNotes/Assets/Fonts/wqy-microhei.ttc#WenQuanYi Micro Hei") },
                    new FontFallback { FontFamily = new FontFamily("PingFang SC") },
                    new FontFallback { FontFamily = new FontFamily("sans-serif") }
                }
            });
    }
}
