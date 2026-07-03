using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ChildNotes.Controls;
using ChildNotes.Infrastructure;
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

        _current = this;
        DevLogger.Log("Startup", "OnFrameworkInitializationCompleted start");
        ReleaseLogger.Info("Startup", "OnFrameworkInitializationCompleted start");
        try
        {
            Batteries_V2.Init();
            DevLogger.Log("Startup", $"Batteries_V2.Init: {sw.ElapsedMilliseconds}ms");

            // 启动时尝试恢复登录会话（30 天滑动过期）
            var restored = TryRestoreSession();
            DevLogger.Log("Startup", $"TryRestoreSession: {sw.ElapsedMilliseconds}ms (restored={restored})");

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow ??= new MainWindow();
                DevLogger.Log("Startup", $"Lifetime=Desktop, MainWindow created: {sw.ElapsedMilliseconds}ms");
                if (restored) EnterMainShell(desktop.MainWindow);
                else ShowLogin(desktop.MainWindow);
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
            {
                DevLogger.Log("Startup", $"Lifetime=SingleView: {sw.ElapsedMilliseconds}ms");
                // 移动端必须先创建 RootContainer（无论恢复成功与否都需要）
                if (_rootContainer is null)
                {
                    _rootContainer = new RootContainer();
                    singleViewPlatform.MainView = _rootContainer;
                    DevLogger.Log("Startup", $"RootContainer set: {sw.ElapsedMilliseconds}ms");
                }
                if (restored) EnterMainShell(null);
                else ShowLogin(singleViewPlatform);
            }
            else
            {
                DevLogger.Log("App", "Lifetime=Unknown: " + ApplicationLifetime?.GetType().Name);
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
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// 启动时尝试从持久化会话恢复登录态。
    /// 成功则同步 AppState / BabyList，失败返回 false（走登录页）。
    /// 任何异常都吞掉并回退到登录页，避免恢复逻辑阻断启动。
    /// </summary>
    private bool TryRestoreSession()
    {
        try
        {
            if (!ServiceProvider.Instance.AuthService.TryRestoreSession())
            {
                ReleaseLogger.Info("App", "Session restore: no valid session, will show login");
                return false;
            }
            ServiceProvider.Instance.BindUserToState();
            ServiceProvider.Instance.BabyService.LoadBabyList();
            ReleaseLogger.Info("App", "Session restored successfully");
            return true;
        }
        catch (Exception ex)
        {
            DevLogger.Log("App", "TryRestoreSession EXCEPTION: " + ex);
            ReleaseLogger.Warn("App", ex, "Session restore failed, falling back to login");
            return false;
        }
    }

    /// <summary>
    /// 恢复登录成功后直接进入主界面（复用 OnLoginSucceeded 的初始化逻辑）。
    /// desktopHost 非空时直接设为 MainWindow.Content；为空则走 RootContainer 模式（移动端）。
    /// </summary>
    private void EnterMainShell(object? desktopHost)
    {
        try
        {
            _shellVm = new MainShellViewModel();
            _shellVm.LogoutRequested += OnLogout;
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
            DevLogger.Log("App", "EnterMainShell done (session restored)");
            ReleaseLogger.Info("App", "EnterMainShell done (session restored)");
        }
        catch (Exception ex)
        {
            DevLogger.Log("App", "EnterMainShell EXCEPTION: " + ex);
            ReleaseLogger.Error("App", ex, "EnterMainShell failed, falling back to login");
            // 恢复失败时回退到登录页
            if (desktopHost is MainWindow w) ShowLogin(w);
            else if (ApplicationLifetime is ISingleViewApplicationLifetime sv) ShowLogin(sv);
        }
    }

    private void ShowLogin(object host)
    {
        DevLogger.Log("App", $"ShowLogin host={host?.GetType().Name}");
        try
        {
            _loginVm = new LoginViewModel();
            _loginVm.LoginSucceeded += OnLoginSucceeded;
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

    private void OnLogout()
    {
        DevLogger.Log("App", "OnLogout");
        ReleaseLogger.Info("App", "User logout");
        if (_shellVm is not null)
        {
            _shellVm.LogoutRequested -= OnLogout;
            _shellVm = null;
        }
        _shellView = null;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is not null)
        {
            ShowLogin(desktop.MainWindow);
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime single)
        {
            ShowLogin(single);
        }
    }

    /// <summary>系统返回键入口：优先关闭当前弹层，返回 false 表示无弹层可关。</summary>
    public bool HandleSystemBack()
    {
        return _shellVm?.HandleSystemBack() ?? false;
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
}
