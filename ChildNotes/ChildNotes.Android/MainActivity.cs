using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Window;
using AndroidX.Core.View;
using Avalonia;
using Avalonia.Android;
using ChildNotes.Android.Services;

namespace ChildNotes.Android;

[Activity(
    Label = "宝宝日记",
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

        // 捕获 Java/ART 层未处理异常（如 Avalonia.Android 无障碍回调中抛出的异常）。
        // .NET 的 AppDomain.UnhandledException 只能捕获托管异常，Java 层异常会直接走 AndroidRuntime
        // 崩溃流程导致闪退且 Serilog 无记录。此处兜底记录到 logcat，便于后续排查。
        // 注意：此回调只能记录日志，无法阻止进程退出（Java 层未处理异常必然导致进程终止）。
        AndroidEnvironment.UnhandledExceptionRaiser += (_, e) =>
        {
            try
            {
                Log.Error("ChildNotes", $"[JavaUnhandled] {e.Exception}");
            }
            catch { }
        };

        // 首次移除无障碍 delegate（详见 DisableAvaloniaAccessibility 注释）
        DisableAvaloniaAccessibility();

        // 启动原生键盘高度监听，通过回调通知 Avalonia 层
        KeyboardHeightService.StartObserving(this);
        KeyboardHeightService.OnKeyboardHeightChanged = OnKeyboardHeightChanged;

        // 注册预测式返回回调（Android 13+ / API 33+）。
        // OnBackPressed 在 API 33+ 已废弃，当前 .NET 10 Android SDK 默认 targetSdk 为 36，
        // 系统不再调用 OnBackPressed，必须用 OnBackInvokedCallback。
        //
        // ★ 关键：OnBackInvokedCallback 必须**动态注册/注销**，不能全程注册。
        // 全程注册会导致预测式返回动画（predictive back animation）失效——
        // 用户从屏幕边缘滑动时看不到任何返回预览，感知为"边缘返回不可用"。
        // 正确做法：有弹层/非首页 Tab 时注册拦截，无弹层时注销恢复系统默认返回。
        //
        // 注：登录页无弹层，不注册回调，系统默认返回（Finish Activity）正常工作。
        if ((int)Build.VERSION.SdkInt >= 33)
        {
            _backCallback = new BackInvokedCallback(this);
            // 订阅 App 的拦截状态变化，动态注册/注销回调
            if (Avalonia.Application.Current is ChildNotes.App app)
            {
                app.InterceptBackChanged += OnInterceptBackChanged;
                // 登录页无弹层，不注册；进入主界面后由事件驱动注册
            }
        }
    }

    /// <summary>
    /// App 层拦截状态变化：true 时注册 OnBackInvokedCallback 拦截返回手势，
    /// false 时注销回调，恢复系统预测式返回动画。
    /// </summary>
    private void OnInterceptBackChanged(bool shouldIntercept)
    {
        if ((int)Build.VERSION.SdkInt < 33 || _backCallback is null) return;
        try
        {
            if (shouldIntercept)
            {
                OnBackInvokedDispatcher.RegisterOnBackInvokedCallback(
                    IOnBackInvokedDispatcher.PriorityDefault, _backCallback);
            }
            else
            {
                OnBackInvokedDispatcher.UnregisterOnBackInvokedCallback(_backCallback);
            }
        }
        catch (System.Exception ex)
        {
            Log.Warn("ChildNotes", $"[BackGesture] 注册/注销回调失败: {ex.Message}");
        }
    }

    protected override void OnResume()
    {
        base.OnResume();

        // Activity 重建后 AvaloniaView.OnAttachedToWindow 会重新绑定 AvaloniaAccessHelper，
        // 必须再次移除 delegate，否则崩溃防护在重建后失效。
        DisableAvaloniaAccessibility();
    }

    /// <summary>
    /// 移除 Avalonia 根 View 的无障碍 delegate，绕过 Avalonia 12.0.5 的崩溃 bug。
    ///
    /// 崩溃路径：MIUI/HyperOS 长按输入框 → 无障碍服务主动遍历查询控件树
    ///   → AvaloniaAccessHelper.OnPopulateNodeForVirtualView（无 try-catch 保护）
    ///   → ToggleNodeInfoProvider.PopulateNodeInfo → GetProvider&lt;IToggleProvider&gt;()
    ///   → peer 不实现 IToggleProvider → throw InvalidOperationException → FATAL EXCEPTION
    ///
    /// 之前尝试对 Window.DecorView 设置 ImportantForAccessibility=NoHideDescendants 无效，
    /// 因为 AccessibilityNodeProvider（虚拟视图机制）通过 ViewCompat.SetAccessibilityDelegate
    /// 绑定在 AvaloniaView 本身，DecorView 的设置影响不到它。
    ///
    /// 正确做法：反射拿到 AvaloniaActivity._view（AvaloniaView 实例，internal 字段），
    /// 对其本身设置 ImportantForAccessibility 并移除 AccessibilityDelegate。
    /// 副作用：TalkBack 无法朗读本应用（对儿童疫苗记录 app 可接受）。
    /// </summary>
    private void DisableAvaloniaAccessibility()
    {
        try
        {
            var viewField = typeof(AvaloniaActivity).GetField("_view",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (viewField?.GetValue(this) is View avaloniaView)
            {
                avaloniaView.ImportantForAccessibility = ImportantForAccessibility.NoHideDescendants;
                ViewCompat.SetAccessibilityDelegate(avaloniaView, null);
            }
            else
            {
                Log.Warn("ChildNotes", "[Accessibility] 未能反射到 _view 字段，崩溃防护未生效");
            }
        }
        catch (System.Exception ex)
        {
            Log.Error("ChildNotes", $"[Accessibility] 移除 AccessibilityDelegate 失败: {ex}");
        }
    }

    protected override void OnDestroy()
    {
        KeyboardHeightService.StopObserving();
        // 取消订阅拦截状态变化事件，避免 Activity 销毁后回调悬空
        if (Avalonia.Application.Current is ChildNotes.App app)
        {
            app.InterceptBackChanged -= OnInterceptBackChanged;
        }
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
    /// 此回调仅在 ShouldInterceptBack=true 时注册（有弹层/非首页 Tab），
    /// 正常情况下 HandleSystemBack 会返回 true（关闭弹层/切回首页）。
    /// 若返回 false（状态已变化但回调尚未注销），则调用 Finish() 兜底退出。
    /// </summary>
    private sealed class BackInvokedCallback : Java.Lang.Object, IOnBackInvokedCallback
    {
        private readonly MainActivity _owner;
        public BackInvokedCallback(MainActivity owner) => _owner = owner;

        public void OnBackInvoked()
        {
            if (Avalonia.Application.Current is ChildNotes.App app && app.HandleSystemBack())
            {
                return; // 弹层已关闭/已切回首页，吞掉返回事件
            }
            // 兜底：回调已注册但无弹层可关（状态变化事件尚未触发注销），主动退出
            _owner.Finish();
        }
    }
}
