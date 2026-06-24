using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using ChildNotes.ViewModels;
using System.Globalization;

namespace ChildNotes.Views;

public partial class BabySetupView : UserControl
{
    public BabySetupView()
    {
        InitializeComponent();
    }

    public static readonly IValueConverter IsBoyConverter = new EqualsConverter("boy");
    public static readonly IValueConverter IsGirlConverter = new EqualsConverter("girl");

    private void OnBoyTap(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BabySetupViewModel vm) vm.SelectGender("boy");
    }

    private void OnGirlTap(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BabySetupViewModel vm) vm.SelectGender("girl");
    }
}
