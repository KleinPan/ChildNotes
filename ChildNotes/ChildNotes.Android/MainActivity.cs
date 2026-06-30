﻿﻿﻿using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Window;
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
    private const string StateKeyTab = "current_tab";
    private IOnBackInvokedCallback? _backCallback;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        // 启动原生键盘高度监听，通过回调通知 Avalonia 层
        KeyboardHeightService.StartObserving(this);
        KeyboardHeightService.OnKeyboardHeightChanged = OnKeyboardHeightChanged;

        // 注册预测式返回回调（Android 13+ / API 33+）。
        // OnBackPressed 在 API 33+ 已废弃，targetSdk=36 时系统不再调用它，必须用 OnBackInvokedCallback。
        // 注册后，侧滑返回手势会先经 HandleSystemBack() 判断是否拦截：
        //   - 有弹层打开 → 吞掉事件，关闭弹层，不退出应用
        //   - 无弹层 → 不拦截，系统执行默认返回（finish Activity 回桌面）
        if ((int)Build.VERSION.SdkInt >= 33)
        {
            _backCallback = new BackInvokedCallback(this);
            OnBackInvokedDispatcher.RegisterOnBackInvokedCallback(
                IOnBackInvokedDispatcher.PriorityDefault, _backCallback);
        }
    }

    protected override void OnDestroy()
    {
        KeyboardHeightService.StopObserving();
        base.OnDestroy();
    }

    /// <summary>
    /// API ≤32 兜底：旧版系统仍走 OnBackPressed。
    /// API 33+ 走 OnBackInvokedCallback，此方法不会被调用（除非 manifest 显式 enableOnBackInvokedCallback=false）。
    /// 两条路径共用同一个 HandleSystemBack 入口，行为一致。
    /// </summary>
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

    /// <summary>
    /// 系统可能因内存压力回收 Activity，任务列表恢复时会重建。
    /// 保存当前 tab 索引，恢复时还原到用户离开前的页面，避免总回到首页。
    /// </summary>
    protected override void OnSaveInstanceState(Bundle outState)
    {
        base.OnSaveInstanceState(outState);
        if (Avalonia.Application.Current is ChildNotes.App app)
        {
            outState.PutString(StateKeyTab, app.GetCurrentTabId());
        }
    }

    /// <summary>
    /// Activity 重建后恢复 tab。在 OnCreate 之后、UI 渲染之前调用。
    /// </summary>
    protected override void OnRestoreInstanceState(Bundle savedInstanceState)
    {
        base.OnRestoreInstanceState(savedInstanceState);
        var tabId = savedInstanceState.GetString(StateKeyTab);
        if (!string.IsNullOrEmpty(tabId) && Avalonia.Application.Current is ChildNotes.App app)
        {
            app.SwitchToTab(tabId);
        }
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

    /// <summary>
    /// OnBackInvokedCallback 实现：转发到跨平台 HandleSystemBack。
    /// 返回 true 表示应用已处理（吞掉返回事件，不退出 Activity），
    /// 返回 false 表示应用未处理（系统执行默认返回行为）。
    /// </summary>
    private sealed class BackInvokedCallback : Java.Lang.Object, IOnBackInvokedCallback
    {
        private readonly MainActivity _owner;
        public BackInvokedCallback(MainActivity owner) => _owner = owner;

        public void OnBackInvoked()
        {
            if (Avalonia.Application.Current is ChildNotes.App app && app.HandleSystemBack())
            {
                return; // 弹层已关闭，吞掉返回事件
            }
            // 无弹层可关：不拦截，让系统执行默认返回（finish Activity）。
            // 注意：OnBackInvokedCallback 没有"放行"API，不调用任何方法即视为已消费。
            // 若需放行，需调用 Finish() 显式结束 Activity。
            _owner.Finish();
        }
    }
}
