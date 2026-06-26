﻿using System;
using Avalonia;
using Avalonia.Media;

namespace ChildNotes.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
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
