using Avalonia.Controls;
using Avalonia.Input;
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

    //  P1-2: 日期选择器事件（Avalonia DatePicker 使用 SelectedDateChanged + DatePickerSelectedValueChangedEventArgs）  //
    private void OnMonthChanged(object? sender, DatePickerSelectedValueChangedEventArgs e)
    {
        if (sender is DatePicker dp && dp.SelectedDate.HasValue && DataContext is StatisticsViewModel vm)
        {
            var m = $"{dp.SelectedDate.Value:yyyy-MM}";
            if (m != vm.SelectedMonth)
                vm.SelectMonthCommand.Execute(m);
        }
    }

    private void OnYearChanged(object? sender, DatePickerSelectedValueChangedEventArgs e)
    {
        if (sender is DatePicker dp && dp.SelectedDate.HasValue && DataContext is StatisticsViewModel vm)
        {
            var y = $"{dp.SelectedDate.Value:yyyy}";
            if (y != vm.SelectedYear)
                vm.SelectYearCommand.Execute(y);
        }
    }

    private void OnStartDateChanged(object? sender, DatePickerSelectedValueChangedEventArgs e)
    {
        if (sender is DatePicker dp && dp.SelectedDate.HasValue && DataContext is StatisticsViewModel vm)
            vm.StartDate = dp.SelectedDate.Value.DateTime;
    }

    private void OnEndDateChanged(object? sender, DatePickerSelectedValueChangedEventArgs e)
    {
        if (sender is DatePicker dp && dp.SelectedDate.HasValue && DataContext is StatisticsViewModel vm)
        {
            vm.EndDate = dp.SelectedDate.Value.DateTime;
            // 起止日期都选完后自动刷新
            vm.SelectRangeCommand.Execute("range");
        }
    }
}
