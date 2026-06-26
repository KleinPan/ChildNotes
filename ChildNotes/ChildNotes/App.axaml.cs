using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ChildNotes.ViewModels;
using ChildNotes.Views;
using SQLitePCL;

namespace ChildNotes;

public partial class App : Application
{
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

        // 初始化 SQLitePCLRaw（注册 e_sqlite3 原生库 provider）。
        // 必须在 ServiceProvider 首次被访问（即 ShowLogin 内 new LoginViewModel()）之前完成，
        // 否则 Microsoft.Data.Sqlite 打开连接会失败，登录/注册表面无反应。
        Batteries_V2.Init();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow ??= new MainWindow();
            ShowLogin(desktop.MainWindow);
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            ShowLogin(singleViewPlatform);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ShowLogin(object host)
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
    }

    private void OnLoginSucceeded()
    {
        _shellVm = new MainShellViewModel();
        _shellVm.LogoutRequested += OnLogout;
        _shellVm.ActivateHomeAfterLogin();
        _shellView = new MainShellView { DataContext = _shellVm };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is not null)
        {
            desktop.MainWindow.Content = _shellView;
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime single)
        {
            single.MainView = _shellView;
        }

        // 解绑登录事件，避免重复触发
        if (_loginVm is not null)
        {
            _loginVm.LoginSucceeded -= OnLoginSucceeded;
            _loginVm = null;
        }
        _loginView = null;
    }

    private void OnLogout()
    {
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
        e.Handled = true;
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogException(e.Exception, "Task");
        e.SetObserved();
    }

    private static void LogException(Exception? ex, string source)
    {
        // 简单输出到调试输出，避免吞掉异常导致问题难以排查
        System.Diagnostics.Debug.WriteLine($"[UnhandledException:{source}] {ex}");
    }
}
