using Avalonia.Controls;
using Avalonia.Interactivity;
using ChildNotes.ViewModels;

namespace ChildNotes.Views;

public partial class BabySetupView : UserControl
{
    public BabySetupView()
    {
        InitializeComponent();
    }

    private void OnBoyTap(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BabySetupViewModel vm) vm.SelectGender("boy");
    }

    private void OnGirlTap(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BabySetupViewModel vm) vm.SelectGender("girl");
    }
}
