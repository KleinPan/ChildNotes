using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ChildNotes.ViewModels;

namespace ChildNotes.Views;

public partial class MineView : UserControl
{
    public MineView()
    {
        InitializeComponent();
    }

    private void OnAccountCenterTap(object? sender, PointerPressedEventArgs e)
    {
        if (FindShell() is { } vm) vm.OpenAccountCenter();
    }

    private void OnBabyManagerTap(object? sender, PointerPressedEventArgs e)
    {
        if (FindShell() is { } vm) vm.OpenBabyManager();
    }

    private void OnAiAnalysisTap(object? sender, PointerPressedEventArgs e)
    {
        if (FindShell() is { } vm) vm.OpenAiAnalysis();
    }

    private void OnStatisticsTap(object? sender, PointerPressedEventArgs e)
    {
        if (FindShell() is { } vm) vm.OpenStatistics();
    }

    private void OnAppSettingsTap(object? sender, PointerPressedEventArgs e)
    {
        if (FindShell() is { } vm) vm.OpenAppSettings();
    }

    private void OnSyncSettingsTap(object? sender, PointerPressedEventArgs e)
    {
        if (FindShell() is { } vm) vm.OpenSyncSettings();
    }

    private void OnInAppMessageTap(object? sender, PointerPressedEventArgs e)
    {
        if (FindShell() is { } vm) vm.OpenInAppMessage();
    }

    private void OnHelpTap(object? sender, PointerPressedEventArgs e)
    {
        if (FindShell() is { } vm) vm.OpenHelp();
    }

    private void OnAboutTap(object? sender, PointerPressedEventArgs e)
    {
        if (FindShell() is { } vm) vm.OpenAbout();
    }

    private void OnDeveloperOptionsTap(object? sender, PointerPressedEventArgs e)
    {
        if (FindShell() is { } vm) vm.OpenDeveloperOptions();
    }

    private MainShellViewModel? FindShell()
    {
        var shell = this.FindAncestorOfType<UserControl>();
        while (shell is not null && shell.DataContext is not MainShellViewModel)
        {
            shell = shell.FindAncestorOfType<UserControl>();
        }
        return shell?.DataContext as MainShellViewModel;
    }
}
