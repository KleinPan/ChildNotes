using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using ChildNotes.Models;
using ChildNotes.ViewModels;

namespace ChildNotes.Views;

public partial class FeedingView : UserControl
{
    public FeedingView()
    {
        InitializeComponent();
    }

    public static readonly IValueConverter IsFeedConverter = new EqualsConverter(RecordType.Feed);
    public static readonly IValueConverter IsSleepConverter = new EqualsConverter(RecordType.Sleep);
    public static readonly IValueConverter IsTempConverter = new EqualsConverter(RecordType.Temperature);
    public static readonly IValueConverter IsGrowthConverter = new EqualsConverter(RecordType.Growth);
    public static readonly IValueConverter IsPumpConverter = new EqualsConverter(RecordType.Pump);
    public static readonly IValueConverter IsVaccineConverter = new EqualsConverter(RecordType.Vaccine);
    public static readonly IValueConverter IsActivityConverter = new EqualsConverter(RecordType.Activity);

    private void OnRecordTap(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.Tag is RecordDisplayItem item && DataContext is FeedingViewModel vm)
        {
            vm.EditRecord(item);
        }
    }

    private void OnDeleteTap(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        if (sender is Border border && border.Tag is RecordDisplayItem item && DataContext is FeedingViewModel vm)
        {
            vm.RequestDelete(item);
        }
    }
}
