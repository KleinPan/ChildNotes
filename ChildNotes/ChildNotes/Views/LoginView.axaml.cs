using Avalonia.Controls;
using ChildNotes.ViewModels;

namespace ChildNotes.Views;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();
    }

    public void Bind(LoginViewModel vm)
    {
        DataContext = vm;
        vm.LoginSucceeded += OnLoginSucceeded;
    }

    private void OnLoginSucceeded()
    {
        var shell = new MainShellView { DataContext = new MainShellViewModel() };
        if (App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is not null)
        {
            desktop.MainWindow.DataContext = shell.DataContext;
            desktop.MainWindow.Content = shell;
        }
        else if (App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.ISingleViewApplicationLifetime single)
        {
            single.MainView = shell;
        }
    }
}
