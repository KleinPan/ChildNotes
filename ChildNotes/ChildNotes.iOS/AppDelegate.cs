using Foundation;
using UIKit;
using Avalonia;
using Avalonia.Controls;
using Avalonia.iOS;
using Avalonia.Media;

namespace ChildNotes.iOS;

// The UIApplicationDelegate for the application. This class is responsible for launching the 
// User Interface of the application, as well as listening (and optionally responding) to 
// application events from iOS.
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
