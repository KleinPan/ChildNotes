using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;

namespace ChildNotes.Android;

[Activity(
    Label = "ChildNotes.Android",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    // AdjustResize: 键盘弹出时窗口自动缩小，弹层内容不会被遮挡
    WindowSoftInputMode = Android.Views.WindowManagerFlags.SoftInputAdjustResize,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
}
