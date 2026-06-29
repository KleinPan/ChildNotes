using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.ViewModels;
using System.Globalization;

namespace ChildNotes.Views;

public partial class BabyManagerView : UserControl
{
    public BabyManagerView()
    {
        InitializeComponent();
    }

    // 判断是否为当前宝宝（参数: Baby, 用 AppState.CurrentBaby.Id 比较）
    public static readonly IValueConverter IsCurrentBabyConverter = new IsCurrentBabyConverter();

    private BabyManagerViewModel? Vm => DataContext as BabyManagerViewModel;

    private void OnBabyItemTapped(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is Baby baby && Vm is { } vm)
        {
            vm.OpenEdit(baby);
        }
    }

    private void OnAddTapped(object? sender, RoutedEventArgs e) => Vm?.OpenAdd();

    private void OnBoyTap(object? sender, RoutedEventArgs e) => Vm?.SelectGender("boy");
    private void OnGirlTap(object? sender, RoutedEventArgs e) => Vm?.SelectGender("girl");

    private void OnCancelEditorTapped(object? sender, PointerPressedEventArgs e) => Vm?.CloseEditorCommand.Execute(null);
    private void OnSaveTapped(object? sender, PointerPressedEventArgs e) => Vm?.SaveCommand.Execute(null);
    private void OnDeleteTapped(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { IsEditing: true } vm) return;
        var editing = ServiceProvider.Instance.AppState.BabyList.FirstOrDefault(b => b.Id == vm.EditingId);
        if (editing is not null) vm.OpenDeleteConfirm(editing);
    }
}

file sealed class IsCurrentBabyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Baby baby)
        {
            return ServiceProvider.Instance.AppState.CurrentBaby?.Id == baby.Id;
        }
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
