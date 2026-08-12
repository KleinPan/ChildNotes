using Avalonia.Controls;
using Avalonia.Interactivity;
using ChildNotes.Services;
using ChildNotes.ViewModels;
using ChildNotes.Shared.Dtos;
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
    /// 移除家庭成员按钮：Tag 保存家庭 DTO（BabyFamilyDto），CommandParameter 保存成员 DTO（BabyMemberDto）。
    /// 仅 owner 可见（由 IsVisible="{Binding !Mine}" 控制，owner 视角下其它成员 Mine=false）。
    /// </summary>
    private void OnRemoveMemberTap(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn
            && btn.Tag is BabyFamilyItem fam
            && btn.CommandParameter is BabyMemberDto member
            && DataContext is FamilyViewModel vm)
        {
            vm.OpenRemoveConfirm(fam, member);
        }
    }

    /// <summary>
    /// 复制宝宝 ID 到剪贴板：宝宝主人可发送给家人，家人凭此 ID 申请加入家庭。
    /// 通过 TopLevel.GetTopLevel 从按钮控件获取 TopLevel，桌面/Android 通用。
    /// </summary>
    private async void OnCopyBabyIdTap(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string babyId } control && DataContext is FamilyViewModel vm)
            await vm.CopyBabyIdAsync(babyId, TopLevel.GetTopLevel(control));
    }
}
