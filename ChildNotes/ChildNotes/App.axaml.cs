using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ChildNotes.Controls;
using ChildNotes.Infrastructure;
using ChildNotes.Services;
using ChildNotes.ViewModels;
using ChildNotes.Views;
using SQLitePCL;

namespace ChildNotes;

public partial class App : Application
{
    private static App? _current;
    private LoginViewModel? _loginVm;
    private MainShellViewModel? _shellVm;
    private LoginView? _loginView;
    private MainShellView? _shellView;
    /// <summary>安卓端根容器：始终作为 ISingleViewApplicationLifetime.MainView 的唯一宿主，
    /// 通过切换 Content 避免替换 MainView 导致视觉树丢失。</summary>
    private RootContainer? _rootContainer;

    /// <summary>隐私协议视图：首次启动或协议版本升级时展示。</summary>
    private PrivacyConsentView? _privacyView;
    /// <summary>平台退出实现：用于隐私协议"不同意"时退出应用。</summary>
    private IApplicationExit? _appExit;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var sw = Stopwatch.StartNew();
        // Release 日志系统初始化（必须最早期，确保后续异常都能记录）
        ReleaseLogger.Initialize();

        // 全局异常处理：避免未捕获异常导致应用崩溃白屏
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        Dispatcher.UIThread.UnhandledException += OnUiThreadUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // i18n 初始化：加载持久化的语言偏好并应用到 Application.Resources。
        // 必须在任何 ViewModel 创建之前完成（MineViewModel 构造时订阅 LanguageChanged）。
        Services.LocaleManager.Instance.Initialize();

        _current = this;
        DevLogger.Log("Startup", "OnFrameworkInitializationCompleted start");
        ReleaseLogger.Info("Startup", "OnFrameworkInitializationCompleted start");
        try
        {
            Batteries_V2.Init();
            DevLogger.Log("Startup", $"Batteries_V2.Init: {sw.ElapsedMilliseconds}ms");

            // 加载动画设置
            LoadAnimationSettings();

            // 平台退出实现注入：桌面端用 DesktopApplicationExit，移动端由平台项目注入
            _appExit = new DesktopApplicationExit();

            // 先确定 Lifetime 并准备容器（RootContainer/MainWindow）
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow ??= new MainWindow();
                DevLogger.Log("Startup", $"Lifetime=Desktop, MainWindow created: {sw.ElapsedMilliseconds}ms");
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
            {
                DevLogger.Log("Startup", $"Lifetime=SingleView: {sw.ElapsedMilliseconds}ms");
                if (_rootContainer is null)
                {
                    _rootContainer = new RootContainer();
                    singleViewPlatform.MainView = _rootContainer;
                    DevLogger.Log("Startup", $"RootContainer set: {sw.ElapsedMilliseconds}ms");
                }
            }
            else
            {
                DevLogger.Log("App", "Lifetime=Unknown: " + ApplicationLifetime?.GetType().Name);
            }

            // 隐私协议检查：未同意或版本升级时展示弹窗，用户同意后才继续启动。
            // 这是上架应用商店的合规要求。
            if (PrivacyConsent.ShouldShow())
            {
                ShowPrivacyConsent();
                DevLogger.Log("Startup", $"Privacy consent shown: {sw.ElapsedMilliseconds}ms");
            }
            else
            {
                ContinueStartupAfterPrivacy();
                DevLogger.Log("Startup", $"Privacy consent already agreed, continue: {sw.ElapsedMilliseconds}ms");
            }
        }
        catch (Exception ex)
        {
            DevLogger.Log("App", "OnFrameworkInitializationCompleted EXCEPTION");
            DevLogger.Log("App", ex);
            throw;
        }

        sw.Stop();
        DevLogger.Log("Startup", $"OnFrameworkInitializationCompleted end: total={sw.ElapsedMilliseconds}ms");
        ReleaseLogger.Info("Startup", $"OnFrameworkInitializationCompleted end: total={sw.ElapsedMilliseconds}ms");

        // ★ Android 端原计划通过订阅事件释放系统启动屏，但经源码验证：
        // OnFrameworkInitializationCompleted 在 Android.App.Application.OnCreate（进程级）中触发，
        // 早于 MainActivity.OnCreate，事件订阅必然太晚，回调永远不会被调用。
        // Android 端改为在 MainActivity.OnCreate 末尾直接置 _isAvaloniaReady=true 释放启动屏。

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// 展示隐私协议弹窗。用户同意后继续启动流程，不同意则退出应用。
    /// 此方法在 OnFrameworkInitializationCompleted 中被调用，不阻塞 UI 线程。
    /// </summary>
    private void ShowPrivacyConsent()
    {
        var vm = new PrivacyConsentViewModel();
        vm.ConsentGiven += OnPrivacyConsentGiven;
        vm.Disagreed += OnPrivacyDisagreed;
        _privacyView = new PrivacyConsentView { DataContext = vm };

        // 将隐私协议视图设为当前内容（覆盖在所有内容之上）
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is not null)
        {
            desktop.MainWindow.Content = _privacyView;
        }
        else if (_rootContainer is not null)
        {
            _rootContainer.SetContent(_privacyView);
        }
    }

    /// <summary>用户同意隐私协议：清理视图，继续启动流程。</summary>
    private void OnPrivacyConsentGiven()
    {
        DevLogger.Log("App", "Privacy consent given, continuing startup");
        ReleaseLogger.Info("App", "Privacy consent given");
        if (_privacyView is not null)
        {
            if (_privacyView.DataContext is PrivacyConsentViewModel vm)
            {
                vm.ConsentGiven -= OnPrivacyConsentGiven;
                vm.Disagreed -= OnPrivacyDisagreed;
            }
            _privacyView = null;
        }
        ContinueStartupAfterPrivacy();
    }

    /// <summary>用户不同意隐私协议：退出应用。</summary>
    private void OnPrivacyDisagreed()
    {
        DevLogger.Log("App", "Privacy consent declined, exiting app");
        ReleaseLogger.Info("App", "Privacy consent declined, exiting");
        _appExit?.Exit();
    }

    private void ContinueStartupAfterPrivacy()
    {
        // 先显示 LoadingView（UI 线程立即返回，用户看到应用首帧）
        var loadingView = new LoadingView();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is not null)
        {
            desktop.MainWindow.Content = loadingView;
        }
        else if (_rootContainer is not null)
        {
            _rootContainer.SetContent(loadingView);
        }
        DevLogger.Log("Startup", "LoadingView shown, starting async init");

        // 后台线程执行初始化（ServiceProvider 静态构造 + DB + 会话恢复）
        // ★ 最小显示时长 500ms：系统启动屏（.NET 冷启动 2-5s）只能放图标不能放文字，
        // 育儿提示只能在这里显示。初始化快于 500ms 时等到 500ms 再切走，让用户瞥见提示；
        // 初始化更慢则按实际时间切走，不额外延迟。
        // 原 1.5s 过长，导致 500ms 完成初始化后仍空等 1s，拖慢启动感知。
        const int minLoadingMs = 500;
        var initStart = Stopwatch.GetTimestamp();
        _ = Task.Run(async () =>
        {
            var restored = await TryRestoreSessionAsync();
            var initElapsed = Stopwatch.GetElapsedTime(initStart);
            var waitMs = Math.Max(0, minLoadingMs - (int)initElapsed.TotalMilliseconds);
            if (waitMs > 0) Thread.Sleep(waitMs);
            DevLogger.Log("Startup", $"TryRestoreSessionAsync (restored={restored}, init={initElapsed.TotalMilliseconds}ms, waited={waitMs}ms)");

            Dispatcher.UIThread.Post(() =>
            {
                // v5：未登录也进入主界面（离线模式可用），登录从 Mine 页等入口触发
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                    && desktop.MainWindow is not null)
                {
                    EnterMainShell(desktop.MainWindow);
                }
                else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
                {
                    EnterMainShell(null);
                }
            });
        });
    }

    /// <summary>
    /// 启动时尝试从持久化会话恢复登录态（离线优先）。
    /// v5：未登录（CloudUserId 空）直接返回 true 进入主界面（离线模式可用）。
    /// 已登录则恢复 CurrentUser；任何异常都吞掉进入主界面，避免阻断启动。
    /// 登录不再是使用 App 的前置条件，登录的作用是开启云同步。
    /// </summary>
    private async Task<bool> TryRestoreSessionAsync()
    {
        try
        {
            // 离线优先：无论是否登录都进入主界面。TryRestoreSessionAsync 内部：
            //   - CloudUserId 空 → 返回 false（离线模式），但仍生成 LocalUserId
            //   - CloudUserId 非空 → 恢复 CurrentUser 返回 true
            var loggedIn = await ServiceProvider.Instance.AuthService.TryRestoreSessionAsync();
            ServiceProvider.Instance.BindUserToState();
            // 会话恢复时确保欢迎消息存在（仅当用户从未有过任何消息时注入一次）
            ServiceProvider.Instance.InAppMessageService.EnsureWelcomeMessage();
            ServiceProvider.Instance.BabyService.LoadBabyList();
            ReleaseLogger.Info("App", loggedIn ? "Session restored (logged in)" : "Offline mode (not logged in)");
            // v5：未登录也进入主界面（离线模式），不强制跳登录页
            return true;
        }
        catch (Exception ex)
        {
            DevLogger.Log("App", "TryRestoreSession EXCEPTION: " + ex);
            ReleaseLogger.Warn("App", ex, "Session restore failed, entering offline mode");
            // 即使恢复失败也进入主界面（离线模式），不阻断用户使用
            return true;
        }
    }

    /// <summary>
    /// 进入主界面（离线模式或已登录均走此入口）。
    /// desktopHost 为 MainWindow 类型时设为 desktop.MainWindow.Content；否则走 RootContainer 模式（移动端）。
    /// v5：未登录也走此方法（离线模式可用），登录从 Mine 页等入口触发。
    /// </summary>
    private void EnterMainShell(object? desktopHost)
    {
        try
        {
            _shellVm = new MainShellViewModel();
            _shellVm.LogoutRequested += OnLogout;
            _shellVm.LoginRequested += OnOpenLogin;
            _shellVm.InterceptBackChanged += OnInterceptBackChanged;
            _shellVm.ActivateHomeAfterLogin();
            _shellView = new MainShellView { DataContext = _shellVm };

            if (desktopHost is MainWindow window)
            {
                window.Content = _shellView;
            }
            else if (_rootContainer is not null)
            {
                _rootContainer.SetContent(_shellView);
            }
            DevLogger.Log("App", "EnterMainShell done");
            ReleaseLogger.Info("App", "EnterMainShell done");
        }
        catch (Exception ex)
        {
            DevLogger.Log("App", "EnterMainShell EXCEPTION: " + ex);
            ReleaseLogger.Error("App", ex, "EnterMainShell failed");
            // v5：不再回退到登录页（登录是可选的），抛出异常便于排查
            throw;
        }
    }

    private void ShowLogin(object host)
    {
        DevLogger.Log("App", $"ShowLogin host={host?.GetType().Name}");
        try
        {
            _loginVm = new LoginViewModel();
            _loginVm.LoginSucceeded += OnLoginSucceeded;
            // 订阅取消事件：本地用户误入登录页可退出回主界面（离线模式）
            _loginVm.CancelRequested += OnLoginCancelled;
            _loginView = new LoginView();
            _loginView.Bind(_loginVm);

            switch (host)
            {
                case MainWindow window:
                    // 桌面端：直接设置 Content（每次切换都替换，桌面端没问题）
                    window.Content = _loginView;
                    break;
                case ISingleViewApplicationLifetime single:
                    // 安卓/iOS 端：使用 RootContainer 容器模式
                    // 只在首次创建时设置 single.MainView，之后只切换容器内部 Content
                    if (_rootContainer is null)
                    {
                        _rootContainer = new RootContainer();
                        single.MainView = _rootContainer;
                        DevLogger.Log("App", "RootContainer set as single.MainView");
                    }
                    _rootContainer.SetContent(_loginView);
                    break;
            }
            DevLogger.Log("App", "ShowLogin done");
        }
        catch (Exception ex)
        {
            DevLogger.Log("App", ex);
            throw;
        }
    }

    /// <summary>
    /// 供 LoginViewModel 直接调用，绕过事件订阅可能丢失的问题（安卓 Activity 重建等场景）。
    /// 通过 _current 单例引用访问，确保无论哪个 LoginViewModel 实例都能切到主界面。
    /// </summary>
    public static void RaiseLoginSucceeded()
    {
        DevLogger.Log("App", "RaiseLoginSucceeded called");
        _current?.OnLoginSucceeded();
    }

    private void OnLoginSucceeded()
    {
        DevLogger.Log("App", "OnLoginSucceeded start");
        ReleaseLogger.Info("App", "Login succeeded, entering main shell");
        try
        {
            _shellVm = new MainShellViewModel();
            DevLogger.Log("App", "MainShellViewModel created");
            _shellVm.LogoutRequested += OnLogout;
            _shellVm.LoginRequested += OnOpenLogin;
            _shellVm.InterceptBackChanged += OnInterceptBackChanged;
            _shellVm.ActivateHomeAfterLogin();
            DevLogger.Log("App", "ActivateHomeAfterLogin done");
            _shellView = new MainShellView { DataContext = _shellVm };
            DevLogger.Log("App", "MainShellView created");

            // 解绑登录事件
            if (_loginVm is not null)
            {
                _loginVm.LoginSucceeded -= OnLoginSucceeded;
                _loginVm = null;
            }
            _loginView = null;

            // 切换视图到主界面
            SwitchToMainView();

            DevLogger.Log("App", "OnLoginSucceeded done");
        }
        catch (Exception ex)
        {
            DevLogger.Log("App", "OnLoginSucceeded EXCEPTION");
            DevLogger.Log("App", ex);
            ReleaseLogger.Error("App", ex, "OnLoginSucceeded failed");
            throw;
        }
    }

    /// <summary>
    /// 用户取消登录（点击登录页返回按钮），切回主界面（离线模式）。
    /// 仅切换视图，不重建 MainShellViewModel，保留用户之前的 tab 状态。
    /// </summary>
    private void OnLoginCancelled()
    {
        DevLogger.Log("App", "OnLoginCancelled: switch back to main shell (offline mode)");
        ReleaseLogger.Info("App", "Login cancelled, returning to main shell in offline mode");

        // 解绑登录事件
        if (_loginVm is not null)
        {
            _loginVm.LoginSucceeded -= OnLoginSucceeded;
            _loginVm.CancelRequested -= OnLoginCancelled;
            _loginVm = null;
        }
        _loginView = null;

        // 切回主界面。_shellVm 在 OnFrameworkInitializationCompleted/EnterMainShell 时已创建，
        // 此处直接切视图，不重建 ViewModel，保留 tab 状态。
        if (_shellView is not null)
        {
            SwitchToMainView();
        }
        else
        {
            // 极端兜底：_shellView 为 null（首次启动直接进登录页，不应发生），走 EnterMainShell 创建。
            EnterMainShell(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null);
        }
    }

    /// <summary>切换到主界面（区分桌面/移动端）</summary>
    private void SwitchToMainView()
    {
        DevLogger.Log("App", "SwitchToMainView begin");
        try
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow is not null)
            {
                desktop.MainWindow.Content = _shellView;
                DevLogger.Log("App", "Set MainWindow.Content = shellView");
            }
            else if (_rootContainer is not null)
            {
                // 安卓/iOS：只切容器内容，不碰 single.MainView
                _rootContainer.SetContent(_shellView);
                DevLogger.Log("App", "RootContainer.SetContent(shellView)");
            }
            DevLogger.Log("App", "SwitchToMainView done");
        }
        catch (Exception ex)
        {
            DevLogger.Log("App", "SwitchToMainView EXCEPTION");
            DevLogger.Log("App", ex);
            throw;
        }
    }

    private async void OnLogout()
    {
        DevLogger.Log("App", "OnLogout");
        ReleaseLogger.Info("App", "User logout");
        if (_shellVm is not null)
        {
            _shellVm.LogoutRequested -= OnLogout;
            _shellVm.LoginRequested -= OnOpenLogin;
            _shellVm.InterceptBackChanged -= OnInterceptBackChanged;
            _shellVm = null;
        }
        _shellView = null;

        // v5：登出 = 清空 Token + CloudUserId，但保留业务数据，返回主界面（离线模式）。
        // 不跳登录页：登录是可选的，登出后用户可继续离线使用本地 SQLite。
        try
        {
            await ServiceProvider.Instance.AuthService.LogoutAsync();
        }
        catch (Exception ex)
        {
            DevLogger.Log("App", "LogoutAsync failed (non-fatal): " + ex.Message);
        }

        // 进入主界面（离线模式），清空 BabyList 后重新加载本地数据
        ServiceProvider.Instance.AppState.Clear();
        try
        {
            ServiceProvider.Instance.BabyService.LoadBabyList();
        }
        catch (Exception ex)
        {
            DevLogger.Log("App", "LoadBabyList after logout failed: " + ex.Message);
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow is not null)
            {
                EnterMainShell(desktop.MainWindow);
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
            {
                EnterMainShell(null);
            }
        });
    }

    /// <summary>
    /// 未登录状态下从账户中心请求登录：切换到登录页。
    /// 与 OnLogout 对称：OnLogout 是登出后回主界面（离线模式），
    /// OnOpenLogin 是主动请求登录，切到登录页。
    /// 登录成功后 OnLoginSucceeded 会重新进入主界面。
    /// </summary>
    private void OnOpenLogin()
    {
        DevLogger.Log("App", "OnOpenLogin: switch to login page");
        ReleaseLogger.Info("App", "User requested login from AccountCenter");
        Dispatcher.UIThread.Post(() =>
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow is not null)
            {
                ShowLogin(desktop.MainWindow);
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
            {
                ShowLogin(singleViewPlatform);
            }
        });
    }

    /// <summary>系统返回键入口：优先关闭当前弹层，返回 false 表示无弹层可关。</summary>
    public bool HandleSystemBack()
    {
        return _shellVm?.HandleSystemBack() ?? false;
    }

    /// <summary>
    /// 当前是否需要拦截系统返回（有弹层打开/非首页 Tab）。
    /// Android 端据此动态注册/注销 OnBackInvokedCallback，避免全程注册导致预测式返回动画失效。
    /// </summary>
    public bool ShouldInterceptBack => _shellVm?.ShouldInterceptBack ?? false;

    /// <summary>
    /// 拦截状态变化事件（仅在 shell 创建后可用）。
    /// Android 端订阅此事件，在 true 时注册 OnBackInvokedCallback，false 时注销。
    /// </summary>
    public event Action<bool>? InterceptBackChanged;

    /// <summary>转发 shell VM 的拦截状态变化到 App 级事件，供 Android 端订阅。</summary>
    private void OnInterceptBackChanged(bool shouldIntercept)
    {
        InterceptBackChanged?.Invoke(shouldIntercept);
    }

    /// <summary>
    /// 获取当前活动 tab 的字符串标识（home/feeding/growth/mine）。
    /// 供 MainActivity.OnSaveInstanceState 保存 UI 状态，Activity 重建后还原。
    /// </summary>
    public string? GetCurrentTabId() => _shellVm?.GetCurrentTabId();

    /// <summary>
    /// 供 MainActivity.OnRestoreInstanceState 调用，Activity 重建后还原 tab。
    /// </summary>
    public void SwitchToTab(string tabId) => _shellVm?.RestoreTab(tabId);

    /// <summary>
    /// 安卓端原生键盘高度变化回调（由 MainActivity 通过 ViewTreeObserver 驱动）。
    /// 高度单位为 Avalonia 逻辑像素（已在安卓端用 DisplayMetrics.Density 转换）。
    /// 桌面端永远不会调用此方法。
    /// </summary>
    public void OnAndroidKeyboardHeightChanged(int logicalHeight)
    {
        KeyboardHeightProvider.UpdateAndroidHeight(logicalHeight);
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        LogException(e.ExceptionObject as Exception, "AppDomain");
    }

    private static void OnUiThreadUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogException(e.Exception, "UIThread");
        // Release 构建标记 Handled 防止崩溃；Debug 下不标记，让异常直接暴露便于排查。
#if !DEBUG
        e.Handled = true;
#endif
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogException(e.Exception, "Task");
        e.SetObserved();
    }

    private static void LogException(Exception? ex, string source)
    {
        if (ex is null)
        {
            DevLogger.Log("EX:" + source, "exception object is null");
            ReleaseLogger.Error("EX:" + source, "exception object is null");
            System.Diagnostics.Debug.WriteLine($"[UnhandledException:{source}] <null>");
            return;
        }
        DevLogger.Log("EX:" + source, ex);
        ReleaseLogger.Error("EX:" + source, ex, $"Unhandled exception from {source}");
        System.Diagnostics.Debug.WriteLine($"[UnhandledException:{source}] {ex}");
    }

    /// <summary>
    /// 从开发者选项配置中加载动画设置，应用到 AnimationService 全局开关。
    /// </summary>
    private void LoadAnimationSettings()
    {
        try
        {
            var config = DeveloperPreferences.Load();
            AnimationService.IsEnabled = config.EnableAnimations;
            DevLogger.Log("Startup", $"Animation settings loaded: EnableAnimations={config.EnableAnimations}");
            ReleaseLogger.Info("Startup", $"Animation settings loaded: EnableAnimations={config.EnableAnimations}");
        }
        catch (Exception ex)
        {
            // 加载失败时使用默认值（开启动画）
            AnimationService.IsEnabled = true;
            DevLogger.Log("Startup", $"Failed to load animation settings, using default (enabled): {ex.Message}");
        }
    }
}
