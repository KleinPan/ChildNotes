using System.Collections.ObjectModel;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Services;
using ChildNotes.Shared.Constants;
using BabyFamilyItem = ChildNotes.Shared.Dtos.BabyFamilyDto;

namespace ChildNotes.ViewModels;

/// <summary>
/// 家人管理 ViewModel：在线查看/修改自己角色，并支持通过宝宝 ID 加入家庭。
/// 需要后端服务可用；离线时提示用户启用同步。
/// </summary>
public partial class FamilyViewModel : ViewModelBase
{
    private readonly FamilyApiClient _api = ServiceProvider.Instance.FamilyApiClient;
    private readonly LocaleManager _locale = LocaleManager.Instance;
    private string _editingBabyName = string.Empty;

    /// <summary>展开后的家庭列表（每个宝宝一个家庭）。</summary>
    public ObservableCollection<BabyFamilyItem> Families { get; } = new();

    /// <summary>角色选项。</summary>
    public IReadOnlyList<RoleOption> RoleOptions => FamilyRoles.All;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private string _emptyHint = string.Empty;

    // 加入家庭表单
    [ObservableProperty] private bool _isJoinOpen;
    [ObservableProperty] private string _joinBabyId = string.Empty;
    [ObservableProperty] private string _joinRoleCode = "other";
    [ObservableProperty] private string _joinError = string.Empty;

    // 修改角色表单
    [ObservableProperty] private bool _isRoleEditorOpen;
    [ObservableProperty] private string _roleEditorTitle = string.Empty;
    [ObservableProperty] private string _editingBabyId = string.Empty;
    [ObservableProperty] private string _editingRoleCode = "other";

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
        // 刷新角色编辑器标题（如果打开）
        if (IsRoleEditorOpen)
            RoleEditorTitle = string.Format(_locale.GetString("Family_RoleEditorTitle", "我的角色 · {0}"), _editingBabyName);
        else
            RoleEditorTitle = _locale.GetString("Family_MyRole", "我的角色");
        // 刷新空状态提示（仅刷新默认未加载状态）
        if (!IsLoading && !HasData && Families.Count == 0)
            EmptyHint = _locale.GetString("Family_EmptyNotLoaded", "尚未加载");
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        EmptyHint = _locale.GetString("Family_Loading", "加载中…");
        try
        {
            var list = await _api.ListFamiliesAsync();
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

    // ===== 加入家庭 =====
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
        var result = await _api.JoinFamilyAsync(babyId, JoinRoleCode);
        if (result is null)
        {
            JoinError = _locale.GetString("Family_ErrJoinFailed", "加入失败，请检查宝宝 ID 或网络");
            return;
        }
        IsJoinOpen = false;
        DisplayToast(string.Format(_locale.GetString("Family_JoinedToast", "已加入，角色：{0}"), FamilyRoles.GetRoleName(JoinRoleCode)));
        await LoadAsync();
    }

    // ===== 修改我的角色 =====
    public void OpenRoleEditor(BabyFamilyItem family)
    {
        // 找到当前用户在此家庭的成员记录
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

    // ===== 复制宝宝 ID =====
    // 家人管理需要宝宝 ID 才能加入家庭，但 UI 之前无处可复制。
    // 由家庭卡片上的"复制 ID"按钮调用，把宝宝 ID 写入系统剪贴板。

    /// <summary>
    /// 复制指定宝宝 ID 到系统剪贴板，并提示用户。
    /// 通过 TopLevel 代理获取 Clipboard，避免 ViewModel 直接依赖 Avalonia 控件。
    /// </summary>
    public async Task CopyBabyIdAsync(string babyId)
    {
        if (string.IsNullOrWhiteSpace(babyId))
        {
            DisplayToast("宝宝 ID 为空");
            return;
        }
        var clipboard = ServiceProvider.Instance.MainView?.Clipboard;
        if (clipboard is null)
        {
            DisplayToast("剪贴板不可用");
            return;
        }
        await clipboard.SetTextAsync(babyId);
        DisplayToast("宝宝 ID 已复制");
    }
}
