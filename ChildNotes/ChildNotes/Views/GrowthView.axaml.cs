using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using ChildNotes.ViewModels;

namespace ChildNotes.Views;

public partial class GrowthView : UserControl
{
    public GrowthView()
    {
        InitializeComponent();
    }

    public static readonly IValueConverter IsEditingConverter = new FuncValueConverter<string, bool>(
        s => s == "编辑成长时刻");

    private void OnAddMilestone(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is GrowthViewModel vm)
        {
            vm.AddMilestoneCommand.Execute(null);
        }
    }

    private void OnMilestoneTap(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && DataContext is GrowthViewModel vm)
        {
            if (border.DataContext is MilestoneDisplayItem item)
            {
                vm.EditMilestone(item);
                return;
            }
            if (border.Tag is MilestoneDisplayItem tagItem)
            {
                vm.EditMilestone(tagItem);
            }
        }
    }
}
