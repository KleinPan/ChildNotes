using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Services;
using ChildNotes.Shared.Constants;
using ChildNotes.Shared.Dtos;
using BabyFamilyItem = ChildNotes.Shared.Dtos.BabyFamilyDto;
using JoinRequestItem = ChildNotes.Shared.Dtos.FamilyJoinRequestDto;

namespace ChildNotes.ViewModels;

/// <summary>
/// 家人管理 ViewModel：在线查看/修改自己角色，支持通过宝宝 ID 申请加入家庭，
/// owner 可移除家庭成员 / 审批加入申请。
/// 需要后端服务可用；离线时提示用户启用同步。
/// </summary>
public partial class FamilyViewModel : ViewModelBase
{
    private readonly FamilyApiClient _api = ServiceProvider.Instance.FamilyApiClient;
    private readonly LocaleManager _locale = LocaleManager.Instance;
    private string _editingBabyName = string.Empty;

    /// <summary>展开后的家庭列表（每个宝宝一个家庭）。</summary>
    public ObservableCollection<BabyFamilyItem> Families { get; } = new();

    /// <summary>待审批的加入申请列表（仅 owner 可见）。</summary>
    public ObservableCollection<JoinRequestItem> PendingRequests { get; } = new();

    /// <summary>我的加入申请历史（含已处理）。</summary>
    public ObservableCollection<JoinRequestItem> MyRequests { get; } = new();

    /// <summary>角色选项。</summary>
    public IReadOnlyList<RoleOption> RoleOptions => FamilyRoles.All;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private string _emptyHint = string.Empty;
    [ObservableProperty] private bool _hasPendingRequests;
    [ObservableProperty] private bool _hasMyRequests;

    // 加入家庭表单（改为提交申请）
    [ObservableProperty] private bool _isJoinOpen;
    [ObservableProperty] private string _joinBabyId = string.Empty;
    [ObservableProperty] private string _joinRoleCode = "other";
    [ObservableProperty] private string _joinError = string.Empty;

    // 修改角色表单
    [ObservableProperty] private bool _isRoleEditorOpen;
    [ObservableProperty] private string _roleEditorTitle = string.Empty;
    [ObservableProperty] private string _editingBabyId = string.Empty;
    [ObservableProperty] private string _editingRoleCode = "other";

    // 移除成员确认弹窗
    [ObservableProperty] private bool _isRemoveConfirmOpen;
    [ObservableProperty] private string _removeConfirmTitle = string.Empty;
    [ObservableProperty] private string _removeConfirmBody = string.Empty;
    private string _removeTargetBabyId = string.Empty;
    private string _removeTargetUserId = string.Empty;
    private string _removeTargetNickName = string.Empty;

    public FamilyViewModel()
    {
        Title = _locale.GetString("Family_Title", "家人管理");
        EmptyHint = _locale.GetString("Family_EmptyNotLoaded", "尚未加载");
        RoleEditorTitle = _locale.GetString("Family_MyRole", "我的角色");
        _locale.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(AppLanguage lang)
    {
        Title = _locale.GetString("Family_Title", "家人管理");
        if (IsRoleEditorOpen)
            RoleEditorTitle = string.Format(_locale.GetString("Family_RoleEditorTitle", "我的角色 · {0}"), _editingBabyName);
        else
            RoleEditorTitle = _locale.GetString("Family_MyRole", "我的角色");
        if (!IsLoading && !HasData && Families.Count == 0)
            EmptyHint = _locale.GetString("Family_EmptyNotLoaded", "尚未加载");
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        EmptyHint = _locale.GetString("Family_Loading", "加载中…");
        try
        {
            var listTask = _api.ListFamiliesAsync();
            var pendingTask = _api.ListPendingJoinRequestsAsync();
            var mineTask = _api.ListMyJoinRequestsAsync();
            await Task.WhenAll(listTask, pendingTask, mineTask);

            var list = await listTask;
            Families.Clear();
            if (list is null)
            {
                HasData = false;
                EmptyHint = _locale.GetString("Family_EmptyNoServer", "无法连接服务器，请先在『数据同步』中配置并启用");
                return;
            }
            foreach (var f in list) Families.Add(f);
            HasData = Families.Count > 0;
            EmptyHint = HasData ? "" : _locale.GetString("Family_EmptyNoFamily", "还没有加入任何家庭");

            // 待审批申请（owner 视角）
            var pending = await pendingTask;
            PendingRequests.Clear();
            if (pending is not null)
            {
                foreach (var r in pending) PendingRequests.Add(r);
            }
            HasPendingRequests = PendingRequests.Count > 0;

            // 我的申请历史
            var mine = await mineTask;
            MyRequests.Clear();
            if (mine is not null)
            {
                foreach (var r in mine) MyRequests.Add(r);
            }
            HasMyRequests = MyRequests.Count > 0;
        }
        catch (Exception ex)
        {
            DevLogger.Log("Family", ex);
            HasData = false;
            EmptyHint = string.Format(_locale.GetString("Family_EmptyError", "加载失败：{0}"), ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task Refresh()
    {
        await LoadAsync();
        DisplayToast(_locale.GetString("Family_Refreshed", "已刷新"));
    }

    // ===== 申请加入家庭（原直接加入改为提交申请） =====
    public void OpenJoin()
    {
        JoinBabyId = string.Empty;
        JoinRoleCode = "other";
        JoinError = string.Empty;
        IsJoinOpen = true;
    }

    [RelayCommand]
    private void CloseJoin() => IsJoinOpen = false;

    public void SelectJoinRole(string code) => JoinRoleCode = code;

    [RelayCommand]
    private async Task ConfirmJoin()
    {
        JoinError = string.Empty;
        var babyId = JoinBabyId.Trim();
        if (string.IsNullOrEmpty(babyId))
        {
            JoinError = _locale.GetString("Family_ErrIdEmpty", "请输入宝宝 ID");
            return;
        }
        var result = await _api.RequestJoinAsync(babyId, JoinRoleCode);
        if (result is null)
        {
            JoinError = _locale.GetString("Family_JoinRequestFailed", "申请提交失败，请检查宝宝 ID 或网络");
            return;
        }
        IsJoinOpen = false;
        DisplayToast(_locale.GetString("Family_JoinRequestSubmitted", "申请已提交，等待宝宝主人审批"));
        await LoadAsync();
    }

    // ===== 修改我的角色 =====
    public void OpenRoleEditor(BabyFamilyItem family)
    {
        var me = family.Members.FirstOrDefault(m => m.Mine);
        EditingBabyId = family.BabyId;
        EditingRoleCode = me?.RoleCode ?? "other";
        _editingBabyName = family.BabyName;
        RoleEditorTitle = string.Format(_locale.GetString("Family_RoleEditorTitle", "我的角色 · {0}"), family.BabyName);
        IsRoleEditorOpen = true;
    }

    [RelayCommand]
    private void CloseRoleEditor() => IsRoleEditorOpen = false;

    public void SelectEditingRole(string code) => EditingRoleCode = code;

    [RelayCommand]
    private async Task ConfirmRole()
    {
        var result = await _api.UpdateMyRoleAsync(EditingBabyId, EditingRoleCode);
        if (result is null)
        {
            DisplayToast(_locale.GetString("Family_SaveFailed", "保存失败"));
            return;
        }
        IsRoleEditorOpen = false;
        DisplayToast(string.Format(_locale.GetString("Family_RoleUpdated", "角色已更新为：{0}"), FamilyRoles.GetRoleName(EditingRoleCode)));
        await LoadAsync();
    }

    // ===== owner 移除家庭成员 =====
    /// <summary>打开移除确认弹窗。由 View 通过成员项的"移除"按钮调用。</summary>
    public void OpenRemoveConfirm(BabyFamilyItem family, BabyMemberDto member)
    {
        _removeTargetBabyId = family.BabyId;
        _removeTargetUserId = member.UserId;
        _removeTargetNickName = member.NickName;
        RemoveConfirmTitle = _locale.GetString("Family_RemoveConfirmTitle", "移除家庭成员");
        RemoveConfirmBody = string.Format(
            _locale.GetString("Family_RemoveConfirmBody",
                "确定将「{0}」从「{1}」家庭中移除？移除后该成员将无法查看本家庭数据，若需重新加入需经过你的审批。"),
            member.NickName, family.BabyName);
        IsRemoveConfirmOpen = true;
    }

    [RelayCommand]
    private void CloseRemoveConfirm() => IsRemoveConfirmOpen = false;

    [RelayCommand]
    private async Task ConfirmRemove()
    {
        var ok = await _api.RemoveMemberAsync(_removeTargetBabyId, _removeTargetUserId);
        if (!ok)
        {
            DisplayToast(_locale.GetString("Family_RemoveFailed", "移除失败，请稍后再试"));
            return;
        }
        IsRemoveConfirmOpen = false;
        DisplayToast(string.Format(_locale.GetString("Family_RemovedToast", "已移除「{0}」"), _removeTargetNickName));
        await LoadAsync();
    }

    // ===== owner 审批加入申请 =====
    [RelayCommand]
    private async Task ApproveRequest(JoinRequestItem? req)
    {
        if (req is null) return;
        var result = await _api.ProcessJoinRequestAsync(req.Id, approve: true);
        if (result is null)
        {
            DisplayToast(_locale.GetString("Family_ProcessFailed", "操作失败，请稍后再试"));
            return;
        }
        DisplayToast(string.Format(_locale.GetString("Family_RequestApprovedToast", "已通过「{0}」的申请"), req.ApplicantNickName));
        await LoadAsync();
    }

    [RelayCommand]
    private async Task RejectRequest(JoinRequestItem? req)
    {
        if (req is null) return;
        var result = await _api.ProcessJoinRequestAsync(req.Id, approve: false);
        if (result is null)
        {
            DisplayToast(_locale.GetString("Family_ProcessFailed", "操作失败，请稍后再试"));
            return;
        }
        DisplayToast(string.Format(_locale.GetString("Family_RequestRejectedToast", "已拒绝「{0}」的申请"), req.ApplicantNickName));
        await LoadAsync();
    }

    // ===== 复制宝宝 ID =====
    public async Task CopyBabyIdAsync(string babyId, TopLevel? topLevel)
    {
        if (string.IsNullOrWhiteSpace(babyId))
        {
            DisplayToast("宝宝 ID 为空");
            return;
        }
        var clipboard = topLevel?.Clipboard;
        if (clipboard is null)
        {
            DisplayToast("剪贴板不可用");
            return;
        }
        await clipboard.SetTextAsync(babyId);
        DisplayToast("宝宝 ID 已复制");
    }
}
