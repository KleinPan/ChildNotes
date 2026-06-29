using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

/// <summary>
/// 家人管理 ViewModel：在线查看/修改自己角色，并支持通过宝宝 ID 加入家庭。
/// 需要后端服务可用；离线时提示用户启用同步。
/// </summary>
public partial class FamilyViewModel : ViewModelBase
{
    private readonly FamilyApiClient _api = ServiceProvider.Instance.FamilyApiClient;

    /// <summary>展开后的家庭列表（每个宝宝一个家庭）。</summary>
    public ObservableCollection<BabyFamilyItem> Families { get; } = new();

    /// <summary>角色选项。</summary>
    public IReadOnlyList<RoleOptionItem> RoleOptions => FamilyRoleOptions.All;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private string _emptyHint = "尚未加载";
    [ObservableProperty] private string _toast = string.Empty;
    [ObservableProperty] private bool _showToast;

    // 加入家庭表单
    [ObservableProperty] private bool _isJoinOpen;
    [ObservableProperty] private string _joinBabyId = string.Empty;
    [ObservableProperty] private string _joinRoleCode = "other";
    [ObservableProperty] private string _joinError = string.Empty;

    // 修改角色表单
    [ObservableProperty] private bool _isRoleEditorOpen;
    [ObservableProperty] private string _roleEditorTitle = "我的角色";
    [ObservableProperty] private long _editingBabyId;
    [ObservableProperty] private string _editingRoleCode = "other";

    public FamilyViewModel()
    {
        Title = "家人管理";
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        EmptyHint = "加载中…";
        try
        {
            var list = await _api.ListFamiliesAsync();
            Families.Clear();
            if (list is null)
            {
                HasData = false;
                EmptyHint = "无法连接服务器，请先在『数据同步』中配置并启用";
                return;
            }
            foreach (var f in list) Families.Add(f);
            HasData = Families.Count > 0;
            EmptyHint = HasData ? "" : "还没有加入任何家庭";
        }
        catch (Exception ex)
        {
            DevLogger.Log("Family", ex);
            HasData = false;
            EmptyHint = "加载失败：" + ex.Message;
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
        ShowToastMsg("已刷新");
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
        if (!long.TryParse(JoinBabyId.Trim(), out var babyId) || babyId <= 0)
        {
            JoinError = "请输入有效的宝宝 ID";
            return;
        }
        var result = await _api.JoinFamilyAsync(babyId, JoinRoleCode);
        if (result is null)
        {
            JoinError = "加入失败，请检查宝宝 ID 或网络";
            return;
        }
        IsJoinOpen = false;
        ShowToastMsg($"已加入，角色：{FamilyRoleOptions.GetRoleName(JoinRoleCode)}");
        await LoadAsync();
    }

    // ===== 修改我的角色 =====
    public void OpenRoleEditor(BabyFamilyItem family)
    {
        // 找到当前用户在此家庭的成员记录
        var me = family.Members.FirstOrDefault(m => m.Mine);
        EditingBabyId = family.BabyId;
        EditingRoleCode = me?.RoleCode ?? "other";
        RoleEditorTitle = $"我的角色 · {family.BabyName}";
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
            ShowToastMsg("保存失败");
            return;
        }
        IsRoleEditorOpen = false;
        DisplayToast($"角色已更新为：{FamilyRoleOptions.GetRoleName(EditingRoleCode)}");
        await LoadAsync();
    }

    // DisplayToast 由基类提供；历史代码中 ShowToastMsg 改为调用 DisplayToast
    private void ShowToastMsg(string msg) => DisplayToast(msg);
}
