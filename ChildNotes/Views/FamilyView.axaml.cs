using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ChildNotes.Models;
using ChildNotes.ViewModels;

namespace ChildNotes.Views;

public partial class FamilyView : UserControl
{
    public FamilyView()
    {
        InitializeComponent();
    }

    private void OnBack(object? sender, RoutedEventArgs e)
    {
        if (DataContext is FamilyViewModel vm) vm.Back();
    }

    private void OnCloseAddSheet(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is FamilyViewModel vm) vm.ShowAddSheet = false;
    }

    private void OnSelectNewRole(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is RoleOption role && DataContext is FamilyViewModel vm)
        {
            vm.SelectNewRole(role);
            RefreshRoleSelection(AddRoleList, vm.SelectedRoleCode);
        }
    }

    private void OnCloseRoleSheet(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is FamilyViewModel vm) vm.ShowRoleSheet = false;
    }

    private void OnSelectEditingRole(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is RoleOption role && DataContext is FamilyViewModel vm)
        {
            vm.SelectEditingRole(role);
            RefreshRoleSelection(EditRoleList, vm.EditingRoleCode);
        }
    }

    private void OnEditRoleTap(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is long memberId && DataContext is FamilyViewModel vm)
        {
            var item = FindMember(vm, memberId);
            if (item is not null)
            {
                vm.OpenRoleSheet(item);
                RefreshRoleSelection(EditRoleList, vm.EditingRoleCode);
            }
        }
    }

    private void OnRemoveTap(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is long memberId && DataContext is FamilyViewModel vm)
        {
            var item = FindMember(vm, memberId);
            if (item is not null) vm.OpenRemoveConfirm(item);
        }
    }

    private static FamilyMemberItem? FindMember(FamilyViewModel vm, long memberId)
    {
        foreach (var m in vm.Members)
        {
            if (m.MemberId == memberId) return m;
        }
        return null;
    }

    private static void RefreshRoleSelection(ItemsControl list, string selectedCode)
    {
        foreach (var item in list.Items)
        {
            if (item is RoleOption role)
            {
                var container = list.ContainerFromItem(role);
                if (container is ContentControl cc && cc.Content is Border border)
                {
                    if (role.Code == selectedCode) border.Classes.Add("selected");
                    else border.Classes.Remove("selected");
                }
            }
        }
    }
}
