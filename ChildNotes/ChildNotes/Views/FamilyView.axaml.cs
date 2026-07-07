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

    /// <summary>
    /// 复制宝宝 ID 到剪贴板：宝宝主人可发送给家人，家人凭此 ID 加入家庭。
    /// </summary>
    private async void OnCopyBabyIdTap(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string babyId } && DataContext is FamilyViewModel vm)
            await vm.CopyBabyIdAsync(babyId);
    }
}
