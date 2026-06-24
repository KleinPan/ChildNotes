using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ChildNotes.ViewModels;
using ChildNotes.Views;

namespace ChildNotes;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var loginVm = new LoginViewModel();
            var loginView = new LoginView();
            loginView.Bind(loginVm);
            desktop.MainWindow = new MainWindow
            {
                Content = loginView,
            };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            var loginVm = new LoginViewModel();
            var loginView = new LoginView();
            loginView.Bind(loginVm);
            singleViewPlatform.MainView = loginView;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
