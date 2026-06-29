using Avalonia.Controls;
using Avalonia.Interactivity;
using ChildNotes.Services;
using ChildNotes.ViewModels;
using BabyFamilyItem = ChildNotes.Shared.Dtos.BabyFamilyDto;

namespace ChildNotes.Views;

public partial class FamilyView : UserControl
{
    public FamilyView()
    {
        InitializeComponent();
    }

    private void OnJoinTap(object? sender, RoutedEventArgs e)
    {
        if (DataContext is FamilyViewModel vm) vm.OpenJoin();
    }

    private void OnEditRoleTap(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: BabyFamilyItem fam } && DataContext is FamilyViewModel vm)
            vm.OpenRoleEditor(fam);
    }

    private void OnSelectJoinRoleTap(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string code } && DataContext is FamilyViewModel vm)
            vm.SelectJoinRole(code);
    }

    private void OnSelectEditingRoleTap(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string code } && DataContext is FamilyViewModel vm)
            vm.SelectEditingRole(code);
    }
}
