using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

public partial class FamilyViewModel : ViewModelBase
{
    private readonly BabyService _babyService = ServiceProvider.Instance.BabyService;
    private readonly AppState _state = ServiceProvider.Instance.AppState;

    [ObservableProperty] private string _babyName = string.Empty;
    [ObservableProperty] private bool _hasBaby;
    [ObservableProperty] private bool _showAddSheet;
    [ObservableProperty] private bool _showRoleSheet;
    [ObservableProperty] private bool _showRemoveConfirm;
    [ObservableProperty] private string _newNickName = string.Empty;
    [ObservableProperty] private string _selectedRoleCode = "father";
    [ObservableProperty] private string _selectedRoleName = "爸爸";
    [ObservableProperty] private string _editingRoleCode = "father";
    [ObservableProperty] private string _editingRoleName = "爸爸";
    [ObservableProperty] private long _editingMemberId;
    [ObservableProperty] private long _removingMemberId;
    [ObservableProperty] private string _removingRoleName = string.Empty;
    [ObservableProperty] private string _toastMessage = string.Empty;
    [ObservableProperty] private bool _showToast;

    public ObservableCollection<FamilyMemberItem> Members { get; } = new();
    public ObservableCollection<RoleOption> Roles { get; } = new(FamilyRoles.All);

    public event Action? BackRequested;
    public event Action? AddBabyRequested;

    public void Load()
    {
        var baby = _state.CurrentBaby;
        if (baby is null)
        {
            HasBaby = false;
            BabyName = string.Empty;
            Members.Clear();
            return;
        }

        HasBaby = true;
        BabyName = baby.Name;

        _babyService.EnsureOwnerMember();
        var members = _babyService.GetMembers();
        Members.Clear();
        foreach (var m in members)
        {
            var isMine = m.UserId == _state.UserId;
            Members.Add(new FamilyMemberItem
            {
                MemberId = m.Id,
                NickName = isMine ? "我" : (m.RoleName),
                RoleName = m.RoleName,
                RoleCode = m.RoleCode,
                IsOwner = m.IsOwner,
                IsMine = isMine,
                CreatedAtText = FormatTime(m.CreatedAt),
            });
        }
    }

    [RelayCommand]
    private void OpenAddSheet()
    {
        NewNickName = string.Empty;
        SelectedRoleCode = "father";
        SelectedRoleName = "爸爸";
        ShowAddSheet = true;
    }

    [RelayCommand]
    private void CloseAddSheet()
    {
        ShowAddSheet = false;
    }

    public void SelectNewRole(RoleOption role)
    {
        SelectedRoleCode = role.Code;
        SelectedRoleName = role.Name;
    }

    [RelayCommand]
    private void ConfirmAdd()
    {
        if (string.IsNullOrWhiteSpace(NewNickName) && SelectedRoleCode == "other")
        {
            ShowToastMessage("请输入昵称");
            return;
        }
        var name = string.IsNullOrWhiteSpace(NewNickName) ? SelectedRoleName : NewNickName.Trim();
        _babyService.AddMember(SelectedRoleCode, SelectedRoleName, name);
        ShowAddSheet = false;
        Load();
        ShowToastMessage("已添加家人");
    }

    public void OpenRoleSheet(FamilyMemberItem item)
    {
        if (item.IsOwner) return;
        EditingMemberId = item.MemberId;
        EditingRoleCode = item.RoleCode;
        EditingRoleName = item.RoleName;
        ShowRoleSheet = true;
    }

    [RelayCommand]
    private void CloseRoleSheet()
    {
        ShowRoleSheet = false;
    }

    public void SelectEditingRole(RoleOption role)
    {
        EditingRoleCode = role.Code;
        EditingRoleName = role.Name;
    }

    [RelayCommand]
    private void ConfirmRole()
    {
        _babyService.UpdateMemberRole(EditingMemberId, EditingRoleCode, EditingRoleName);
        ShowRoleSheet = false;
        Load();
        ShowToastMessage("已更新身份");
    }

    public void OpenRemoveConfirm(FamilyMemberItem item)
    {
        if (item.IsOwner) return;
        RemovingMemberId = item.MemberId;
        RemovingRoleName = item.RoleName;
        ShowRemoveConfirm = true;
    }

    [RelayCommand]
    private void CloseRemoveConfirm()
    {
        ShowRemoveConfirm = false;
    }

    [RelayCommand]
    private void ConfirmRemove()
    {
        _babyService.RemoveMember(RemovingMemberId);
        ShowRemoveConfirm = false;
        Load();
        ShowToastMessage("已移除家人");
    }

    [RelayCommand]
    private void GoAddBaby()
    {
        AddBabyRequested?.Invoke();
    }

    public void Back() => BackRequested?.Invoke();

    private async void ShowToastMessage(string msg)
    {
        ToastMessage = msg;
        ShowToast = true;
        await Task.Delay(2000);
        ShowToast = false;
    }

    private static string FormatTime(DateTime dt)
    {
        var diff = DateTime.UtcNow - dt;
        if (diff.TotalMinutes < 1) return "刚刚";
        if (diff.TotalHours < 1) return $"{(int)diff.TotalMinutes}分钟前";
        if (diff.TotalDays < 1) return $"{(int)diff.TotalHours}小时前";
        if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}天前";
        return dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }
}

public sealed class FamilyMemberItem
{
    public long MemberId { get; set; }
    public string NickName { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string RoleCode { get; set; } = string.Empty;
    public bool IsOwner { get; set; }
    public bool IsMine { get; set; }
    public string CreatedAtText { get; set; } = string.Empty;
}
