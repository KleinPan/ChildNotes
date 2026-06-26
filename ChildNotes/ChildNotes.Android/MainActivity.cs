using Android.App;
using Android.Content.PM;
using Android.Views;
using Avalonia;
using Avalonia.Android;

namespace ChildNotes.Android;

[Activity(
    Label = "ChildNotes.Android",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    // AdjustResize: 键盘弹出时窗口自动缩小，弹层内容不会被遮挡
    WindowSoftInputMode = SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    public override void OnAttachedToWindow()
    {
        base.OnAttachedToWindow();
        // 安卓 15+ 强制 edge-to-edge，WindowDecorAdjustResizeForBordersEnabled 需显式启用
        if (Window is not null)
        {
            Window.DecorView.SystemUiVisibilityChange += OnSystemUiVisibilityChange;
        }
    }

    private void OnSystemUiVisibilityChange(object? sender, SystemUiVisibilityChangeEventArgs e)
    {
        // 状态栏变化时强制重新布局
        Window?.DecorView?.RequestLayout();
    }
}
