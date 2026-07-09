using Avalonia.Controls;
using Avalonia.Input;
using ChildNotes.Controls;
using ChildNotes.ViewModels;

namespace ChildNotes.Views;

public partial class StatisticsView : UserControl
{
    public StatisticsView()
    {
        InitializeComponent();
    }

    private void OnTypeTap(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border b && b.DataContext is StatTypeOption opt && DataContext is StatisticsViewModel vm)
            vm.SelectTypeCommand.Execute(opt.Key);
    }

    private void OnRangeTap(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border b && b.DataContext is StatRangeOption opt && DataContext is StatisticsViewModel vm)
            vm.SelectRangeCommand.Execute(opt.Key);
    }

    private void OnMonthChanged(object? sender, DateTimeOffset? e)
    {
        if (sender is DateWheelPicker dp && dp.SelectedDate.HasValue && DataContext is StatisticsViewModel vm)
        {
            var m = $"{dp.SelectedDate.Value:yyyy-MM}";
            if (m != vm.SelectedMonth)
                vm.SelectMonthCommand.Execute(m);
        }
    }

    private void OnYearChanged(object? sender, DateTimeOffset? e)
    {
        if (sender is DateWheelPicker dp && dp.SelectedDate.HasValue && DataContext is StatisticsViewModel vm)
        {
            var y = $"{dp.SelectedDate.Value:yyyy}";
            if (y != vm.SelectedYear)
                vm.SelectYearCommand.Execute(y);
        }
    }

    private void OnStartDateChanged(object? sender, DateTimeOffset? e)
    {
        if (sender is DateWheelPicker dp && dp.SelectedDate.HasValue && DataContext is StatisticsViewModel vm)
            vm.StartDate = dp.SelectedDate.Value.DateTime;
    }

    private void OnEndDateChanged(object? sender, DateTimeOffset? e)
    {
        if (sender is DateWheelPicker dp && dp.SelectedDate.HasValue && DataContext is StatisticsViewModel vm)
        {
            vm.EndDate = dp.SelectedDate.Value.DateTime;
            vm.SelectRangeCommand.Execute("range");
        }
    }
}
