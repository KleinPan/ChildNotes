﻿using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia;
using Avalonia.Android;
using ChildNotes.Android.Services;

namespace ChildNotes.Android;

[Activity(
    Label = "ChildNotes.Android",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/Icon",
    MainLauncher = true,
    // windowSoftInputMode=adjustResize 在 AndroidManifest.xml 中设置（C# 枚举绑定在 .NET 10 Android 变化频繁，XML 更稳定）
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
#pragma warning disable CS0672 // OnBackPressed 在新版 Android 中已废弃，但仍可用
    public override void OnBackPressed()
    {
        if (Avalonia.Application.Current is ChildNotes.App app && app.HandleSystemBack())
        {
            return; // 弹层已关闭，不执行系统默认行为
        }
        base.OnBackPressed(); // 无弹层，交由系统处理
    }
#pragma warning restore CS0672

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        // 启动原生键盘高度监听，通过回调通知 Avalonia 层
        KeyboardHeightService.StartObserving(this);
        KeyboardHeightService.OnKeyboardHeightChanged = OnKeyboardHeightChanged;
    }

    protected override void OnDestroy()
    {
        KeyboardHeightService.StopObserving();
        base.OnDestroy();
    }

    private void OnKeyboardHeightChanged(int heightPx)
    {
        // 通过 Avalonia UI 线程分发到共享层的键盘高度服务
        var app = Avalonia.Application.Current as App;
        app?.Dispatcher?.Post(() =>
        {
            if (app is not null)
                app.OnAndroidKeyboardHeightChanged(heightPx);
        });
    }
}
