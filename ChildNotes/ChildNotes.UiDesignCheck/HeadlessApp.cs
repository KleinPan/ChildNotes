using Avalonia;
using Avalonia.Headless;
using ChildNotes;

namespace ChildNotes.UiDesignCheck;

internal sealed class HeadlessApp : App
{
    public override void OnFrameworkInitializationCompleted()
    {
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<HeadlessApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false
            })
            .WithInterFont()
            .LogToTrace();
}
