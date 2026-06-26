using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
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

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // 全局异常处理：避免未捕获异常导致应用崩溃白屏
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        Dispatcher.UIThread.UnhandledException += OnUiThreadUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _current = this;
        DevLogger.Log("App", "OnFrameworkInitializationCompleted start");
        try
        {
            // 初始化 SQLitePCLRaw（注册 e_sqlite3 原生库 provider）。
            // 必须在 ServiceProvider 首次被访问（即 ShowLogin 内 new LoginViewModel()）之前完成，
            // 否则 Microsoft.Data.Sqlite 打开连接会失败，登录/注册表面无反应。
            Batteries_V2.Init();
            DevLogger.Log("App", "Batteries_V2.Init done");

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow ??= new MainWindow();
                DevLogger.Log("App", "Lifetime=Desktop");
                ShowLogin(desktop.MainWindow);
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
            {
                DevLogger.Log("App", "Lifetime=SingleView");
                ShowLogin(singleViewPlatform);
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

        DevLogger.Log("App", "OnFrameworkInitializationCompleted end");
        base.OnFrameworkInitializationCompleted();
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
                    window.Content = _loginView;
                    break;
                case ISingleViewApplicationLifetime single:
                    single.MainView = _loginView;
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
        try
        {
            _shellVm = new MainShellViewModel();
            DevLogger.Log("App", "MainShellViewModel created");
            _shellVm.LogoutRequested += OnLogout;
            _shellVm.ActivateHomeAfterLogin();
            DevLogger.Log("App", "ActivateHomeAfterLogin done");
            _shellView = new MainShellView { DataContext = _shellVm };
            DevLogger.Log("App", "MainShellView created");

            // 先清空旧视图引用，避免安卓上旧视图阻止新视图挂载到视觉树
            if (_loginVm is not null)
            {
                _loginVm.LoginSucceeded -= OnLoginSucceeded;
                _loginVm = null;
            }
            _loginView = null;

            DevLogger.Log("App", "Setting MainView begin");
            try
            {
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                    && desktop.MainWindow is not null)
                {
                    desktop.MainWindow.Content = _shellView;
                    DevLogger.Log("App", "Set MainWindow.Content = shellView");
                }
                else if (ApplicationLifetime is ISingleViewApplicationLifetime single)
                {
                    DevLogger.Log("App", "About to assign single.MainView");
                    single.MainView = _shellView;
                    DevLogger.Log("App", $"single.MainView assigned: {single.MainView?.GetType().Name ?? "null"}");

                    // 安卓上赋值后可能不立即触发视觉树更新，强制刷新
                    _shellView.InvalidateMeasure();
                    _shellView.InvalidateArrange();
                    DevLogger.Log("App", "Invalidated measure+arrange");

                    // 延迟一帧验证视觉树是否已挂载
                    Dispatcher.UIThread.Post(() =>
                    {
                        DevLogger.Log("App", $"Post-check: Parent={_shellView?.Parent?.GetType().Name ?? "null"}, VisualRoot={_shellView?.GetVisualRoot()?.GetType().Name ?? "null"}, IsVisible={_shellView?.IsVisible}");
                        // 如果还没挂载，再试一次强制布局
                        if (_shellView?.Parent is null)
                        {
                            DevLogger.Log("App", "Post-check: still no parent! Trying force update...");
                            _shellView.UpdateLayout();
                            DevLogger.Log("App", $"Post-check after UpdateLayout: Parent={_shellView?.Parent?.GetType().Name ?? "null"}");
                        }
                    });
                }
                DevLogger.Log("App", "Setting MainView done");
            }
            catch (Exception ex)
            {
                DevLogger.Log("App", "Setting MainView EXCEPTION");
                DevLogger.Log("App", ex);
                throw;
            }

            DevLogger.Log("App", "OnLoginSucceeded done");
        }
        catch (Exception ex)
        {
            DevLogger.Log("App", "OnLoginSucceeded EXCEPTION");
            DevLogger.Log("App", ex);
            throw;
        }
    }

    private void OnLogout()
    {
        DevLogger.Log("App", "OnLogout");
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

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        LogException(e.ExceptionObject as Exception, "AppDomain");
    }

    private static void OnUiThreadUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogException(e.Exception, "UIThread");
        // 开发阶段：不标记 Handled，让异常直接暴露便于排查（默认会崩溃，但能看到完整堆栈）
        // 上线前改为 e.Handled = true;
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogException(e.Exception, "Task");
        e.SetObserved();
    }

    private static void LogException(Exception? ex, string source)
    {
        // 同时写入 DevLogger 浮层和 Debug 输出，便于在 Android 真机无 adb 时排查
        if (ex is null)
        {
            DevLogger.Log("EX:" + source, "exception object is null");
            System.Diagnostics.Debug.WriteLine($"[UnhandledException:{source}] <null>");
            return;
        }
        DevLogger.Log("EX:" + source, ex);
        System.Diagnostics.Debug.WriteLine($"[UnhandledException:{source}] {ex}");
    }
}
