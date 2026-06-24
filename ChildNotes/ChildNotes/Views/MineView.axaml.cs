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

    private void OnAddBabyTap(object? sender, PointerPressedEventArgs e)
    {
        var shell = this.FindAncestorOfType<UserControl>();
        while (shell is not null && shell.DataContext is not MainShellViewModel)
        {
            shell = shell.FindAncestorOfType<UserControl>();
        }
        if (shell?.DataContext is MainShellViewModel vm)
        {
            vm.OpenBabySetup();
        }
    }

    private void OnStatisticsTap(object? sender, PointerPressedEventArgs e)
    {
        if (FindShell() is { } vm) vm.OpenStatistics();
    }

    private void OnPointsTap(object? sender, PointerPressedEventArgs e)
    {
        if (FindShell() is { } vm) vm.OpenPoints();
    }

    private void OnFamilyTap(object? sender, PointerPressedEventArgs e)
    {
        if (FindShell() is { } vm) vm.OpenFamily();
    }

    private void OnAiAnalysisTap(object? sender, PointerPressedEventArgs e)
    {
        if (FindShell() is { } vm) vm.OpenAiAnalysis();
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
