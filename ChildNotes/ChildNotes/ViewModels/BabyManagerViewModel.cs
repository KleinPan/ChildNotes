using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Services;
using ChildNotes.Shared.Constants;
using ChildNotes.Shared.Dtos;
using BabyFamilyItem = ChildNotes.Shared.Dtos.BabyFamilyDto;
using JoinRequestItem = ChildNotes.Shared.Dtos.FamilyJoinRequestDto;

namespace ChildNotes.ViewModels;

/// <summary>
/// 人员管理 ViewModel（合并宝宝管理 + 家人管理）。
/// 顶部宝宝列表，每个宝宝卡片下方展示该宝宝的家庭成员；
/// 底部保留申请加入家庭入口、待审批申请（owner 可见）、我的申请历史。
/// 家人管理为在线功能，需要后端服务可用；离线时不阻塞宝宝列表显示。
/// </summary>
public partial class BabyManagerViewModel : ViewModelBase
{
    private readonly BabyService _babyService = ServiceProvider.Instance.BabyService;
    private readonly AppState _state = ServiceProvider.Instance.AppState;
    private readonly LocaleManager _locale = LocaleManager.Instance;
    private readonly FamilyApiClient _familyApi = ServiceProvider.Instance.FamilyApiClient;
    private string _editingBabyName = string.Empty;

    public ObservableCollection<Baby> BabyList { get; } = new();

    /// <summary>每个宝宝的家庭成员列表（按 BabyId 索引，用于 join 到 BabyList）。</summary>
    public ObservableCollection<BabyFamilyItem> Families { get; } = new();

    /// <summary>待审批的加入申请列表（仅 owner 可见）。</summary>
    public ObservableCollection<JoinRequestItem> PendingRequests { get; } = new();

    /// <summary>我的加入申请历史（含已处理）。</summary>
    public ObservableCollection<JoinRequestItem> MyRequests { get; } = new();

    /// <summary>角色选项。</summary>
    public IReadOnlyList<RoleOption> RoleOptions => FamilyRoles.All;

    [ObservableProperty] private bool _hasBaby;
    [ObservableProperty] private bool _isEditorOpen;
    [ObservableProperty] private bool _isEditing;          // true=编辑, false=新增
    [ObservableProperty] private bool _isDeleteConfirmOpen;
    [ObservableProperty] private string _editorTitle = LocaleManager.Instance.GetString("BabyMgr_AddTitle", "添加宝宝");

    // 编辑表单字段
    [ObservableProperty] private string _editingId = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _gender = "boy";
    [ObservableProperty] private DateTime? _birthDate;
    [ObservableProperty] private string _deleteConfirmName = string.Empty;

    // 头像相关
    [ObservableProperty] private Bitmap? _avatarBitmap;
    [ObservableProperty] private bool _hasAvatar;

    // ===== 家人管理相关状态 =====
    [ObservableProperty] private bool _hasPendingRequests;
    [ObservableProperty] private bool _hasMyRequests;
    [ObservableProperty] private bool _hasFamilies;

    // 加入家庭表单（提交申请）
    [ObservableProperty] private bool _isJoinOpen;
    [ObservableProperty] private string _joinBabyId = string.Empty;
    [ObservableProperty] private string _joinRoleCode = "other";
    [ObservableProperty] private string _joinError = string.Empty;

    // 修改角色表单
    [ObservableProperty] private bool _isRoleEditorOpen;
    [ObservableProperty] private string _roleEditorTitle = string.Empty;
    [ObservableProperty] private string _editingRoleCode = "other";
    [ObservableProperty] private string _editingBabyId = string.Empty;

    // 移除成员确认弹窗
    [ObservableProperty] private bool _isRemoveConfirmOpen;
    [ObservableProperty] private string _removeConfirmTitle = string.Empty;
    [ObservableProperty] private string _removeConfirmBody = string.Empty;
    private string _removeTargetBabyId = string.Empty;
    private string _removeTargetUserId = string.Empty;
    private string _removeTargetNickName = string.Empty;

    /// <summary>无头像时显示的性别 emoji 占位符。</summary>
    public string GenderEmoji => Gender switch
    {
        "girl" => "\U0001F467",  // 👧
        _ => "\U0001F466",        // 👦
    };

    public event Action? BabyChanged;        // 增删改后通知外部刷新

    public BabyManagerViewModel()
    {
        Title = _locale.GetString("BabyMgr_Title", "人员管理");
        _locale.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(AppLanguage lang)
    {
        Title = _locale.GetString("BabyMgr_Title", "人员管理");
        // 编辑器打开时刷新标题文案；关闭时由 OpenAdd/OpenEdit 重新设置
        if (IsEditorOpen)
            EditorTitle = IsEditing
                ? _locale.GetString("BabyMgr_EditTitle", "编辑宝宝")
                : _locale.GetString("BabyMgr_AddTitle", "添加宝宝");
        if (IsRoleEditorOpen)
            RoleEditorTitle = string.Format(_locale.GetString("Family_RoleEditorTitle", "我的角色 · {0}"), _editingBabyName);
        else
            RoleEditorTitle = _locale.GetString("Family_MyRole", "我的角色");
    }

    public void Load()
    {
        BabyList.Clear();
        var list = _babyService.LoadBabyList();
        foreach (var b in list) BabyList.Add(b);
        HasBaby = list.Count > 0;
    }

    /// <summary>
    /// 异步加载：DB 查询放到后台线程，UI 线程仅做集合填充。
    /// 同时并行拉取家庭成员/待审批申请/我的申请（在线数据，失败不阻塞宝宝列表）。
    /// </summary>
    public async Task LoadAsync()
    {
        // 本地宝宝列表：后台线程查询
        var list = await Task.Run(() => _babyService.LoadBabyList());

        // 在线家庭成员/申请：并行拉取，失败时仅记录日志，不阻塞宝宝列表
        List<BabyFamilyItem>? families = null;
        List<JoinRequestItem>? pending = null;
        List<JoinRequestItem>? mine = null;
        try
        {
            var listTask = _familyApi.ListFamiliesAsync();
            var pendingTask = _familyApi.ListPendingJoinRequestsAsync();
            var mineTask = _familyApi.ListMyJoinRequestsAsync();
            await Task.WhenAll(listTask, pendingTask, mineTask);
            families = await listTask;
            pending = await pendingTask;
            mine = await mineTask;
        }
        catch (Exception ex)
        {
            DevLogger.Log("BabyManagerVM", $"加载家庭数据失败（不阻塞宝宝列表）: {ex.Message}");
        }

        // 填充宝宝列表 + join 家庭成员到 Baby.Family
        BabyList.Clear();
        var familyMap = families?.ToDictionary(f => f.BabyId) ?? new();
        foreach (var b in list)
        {
            b.Family = familyMap.TryGetValue(b.Id, out var f) ? f : null;
            BabyList.Add(b);
        }
        HasBaby = list.Count > 0;

        // 填充家庭列表（保留 Families 集合供 UI 直接绑定待审批/我的申请）
        Families.Clear();
        if (families is not null)
        {
            foreach (var f in families) Families.Add(f);
        }
        HasFamilies = Families.Count > 0;

        // 待审批申请（owner 视角）
        PendingRequests.Clear();
        if (pending is not null)
        {
            foreach (var r in pending) PendingRequests.Add(r);
        }
        HasPendingRequests = PendingRequests.Count > 0;

        // 我的申请历史
        MyRequests.Clear();
        if (mine is not null)
        {
            foreach (var r in mine) MyRequests.Add(r);
        }
        HasMyRequests = MyRequests.Count > 0;
    }

    /// <summary>刷新家庭相关数据（家人管理操作后调用）。</summary>
    private async Task RefreshFamiliesAsync()
    {
        try
        {
            var listTask = _familyApi.ListFamiliesAsync();
            var pendingTask = _familyApi.ListPendingJoinRequestsAsync();
            var mineTask = _familyApi.ListMyJoinRequestsAsync();
            await Task.WhenAll(listTask, pendingTask, mineTask);
            var families = await listTask;
            var pending = await pendingTask;
            var mine = await mineTask;

            // 重新 join 到 BabyList
            var familyMap = families?.ToDictionary(f => f.BabyId) ?? new();
            foreach (var b in BabyList)
            {
                b.Family = familyMap.TryGetValue(b.Id, out var f) ? f : null;
            }

            Families.Clear();
            if (families is not null)
            {
                foreach (var f in families) Families.Add(f);
            }
            HasFamilies = Families.Count > 0;

            PendingRequests.Clear();
            if (pending is not null)
            {
                foreach (var r in pending) PendingRequests.Add(r);
            }
            HasPendingRequests = PendingRequests.Count > 0;

            MyRequests.Clear();
            if (mine is not null)
            {
                foreach (var r in mine) MyRequests.Add(r);
            }
            HasMyRequests = MyRequests.Count > 0;
        }
        catch (Exception ex)
        {
            DevLogger.Log("BabyManagerVM", $"刷新家庭数据失败: {ex.Message}");
        }
    }

    public bool IsCurrentBaby(Baby baby) => _state.CurrentBaby?.Id == baby.Id;

    public void OpenAdd()
    {
        IsEditing = false;
        EditorTitle = _locale.GetString("BabyMgr_AddTitle", "添加宝宝");
        EditingId = string.Empty;
        Name = string.Empty;
        Gender = "boy";
        BirthDate = null;
        ErrorMessage = string.Empty;
        ClearAvatar();
        IsEditorOpen = true;
    }

    public void OpenEdit(Baby baby)
    {
        IsEditing = true;
        EditorTitle = _locale.GetString("BabyMgr_EditTitle", "编辑宝宝");
        EditingId = baby.Id;
        Name = baby.Name;
        Gender = baby.Gender;
        // 数据库读出的 BirthDate 是 Unspecified Kind，绑定到 CalendarDatePicker 后再回传
        // 与 DateTime.Today 做差值运算会抛 DateTimeKind 异常，显式指定 Local Kind
        BirthDate = baby.BirthDate.HasValue
            ? DateTime.SpecifyKind(baby.BirthDate.Value.Date, DateTimeKind.Local)
            : null;
        ErrorMessage = string.Empty;
        LoadAvatarFromPath(baby.Avatar);
        IsEditorOpen = true;
    }

    [RelayCommand]
    private void CloseEditor()
    {
        IsEditorOpen = false;
    }

    public void SelectGender(string gender) => Gender = gender;

    [RelayCommand]
    private void Save()
    {
        ErrorMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = _locale.GetString("BabyMgr_ErrName", "请输入宝宝姓名");
            return;
        }
        if (BirthDate is null)
        {
            ErrorMessage = _locale.GetString("BabyMgr_ErrBirthday", "请选择出生日期");
            return;
        }

        if (IsEditing)
        {
            var baby = _state.BabyList.FirstOrDefault(b => b.Id == EditingId);
            if (baby is not null)
            {
                baby.Name = Name.Trim();
                baby.Gender = Gender;
                // 统一转 Local Kind，避免 CalendarDatePicker 回传 Unspecified Kind
                // 在后续与 DateTime.Today 比较时抛 DateTimeKind 异常
                baby.BirthDate = DateTime.SpecifyKind(BirthDate.Value.Date, DateTimeKind.Local);
                baby.Avatar = _pendingAvatarPath ?? baby.Avatar;  // 保存新选择的头像路径
                _babyService.UpdateBaby(baby);
            }
        }
        else
        {
            var avatarPath = _pendingAvatarPath ?? string.Empty;
            _babyService.AddBaby(Name.Trim(), Gender, DateTime.SpecifyKind(BirthDate.Value.Date, DateTimeKind.Local), avatarPath);
        }

        IsEditorOpen = false;
        Load();
        BabyChanged?.Invoke();
    }

    public void OpenDeleteConfirm(Baby baby)
    {
        DeleteConfirmName = baby.Name;
        EditingId = baby.Id;
        IsDeleteConfirmOpen = true;
    }

    /// <summary>
    /// 复制当前编辑/删除上下文中宝宝的 ID（EditingId 字段）到系统剪贴板。
    /// 宝宝主人可发送给家人，家人凭此 ID 申请加入家庭。
    /// 由 View 通过 TopLevel.GetTopLevel(sender) 获取 TopLevel 并传入，桌面/Android 通用。
    /// </summary>
    public async Task CopyEditingIdAsync(TopLevel? topLevel)
    {
        var id = EditingId;
        if (string.IsNullOrWhiteSpace(id))
        {
            DisplayToast(_locale.GetString("BabyMgr_IdEmpty", "宝宝 ID 为空"));
            return;
        }
        var clipboard = topLevel?.Clipboard;
        if (clipboard is null)
        {
            DisplayToast(_locale.GetString("BabyMgr_ClipUnavailable", "剪贴板不可用"));
            return;
        }
        await clipboard.SetTextAsync(id);
        DisplayToast(_locale.GetString("BabyMgr_IdCopied", "宝宝 ID 已复制"));
    }

    /// <summary>
    /// 复制指定宝宝 ID 到剪贴板（供宝宝卡片上的"复制 ID"按钮调用）。
    /// </summary>
    public async Task CopyBabyIdAsync(string babyId, TopLevel? topLevel)
    {
        if (string.IsNullOrWhiteSpace(babyId))
        {
            DisplayToast(_locale.GetString("BabyMgr_IdEmpty", "宝宝 ID 为空"));
            return;
        }
        var clipboard = topLevel?.Clipboard;
        if (clipboard is null)
        {
            DisplayToast(_locale.GetString("BabyMgr_ClipUnavailable", "剪贴板不可用"));
            return;
        }
        await clipboard.SetTextAsync(babyId);
        DisplayToast(_locale.GetString("BabyMgr_IdCopied", "宝宝 ID 已复制"));
    }

    [RelayCommand]
    private void CloseDeleteConfirm()
    {
        IsDeleteConfirmOpen = false;
    }

    [RelayCommand]
    private void ConfirmDelete()
    {
        _babyService.DeleteBaby(EditingId);
        IsDeleteConfirmOpen = false;
        IsEditorOpen = false;
        Load();
        BabyChanged?.Invoke();
    }

    // ==================== 家人管理：申请加入家庭 ====================

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
        var result = await _familyApi.RequestJoinAsync(babyId, JoinRoleCode);
        if (result is null)
        {
            JoinError = _locale.GetString("Family_JoinRequestFailed", "申请提交失败，请检查宝宝 ID 或网络");
            return;
        }
        IsJoinOpen = false;
        DisplayToast(_locale.GetString("Family_JoinRequestSubmitted", "申请已提交，等待宝宝主人审批"));
        await RefreshFamiliesAsync();
    }

    // ==================== 家人管理：修改我的角色 ====================

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
        var result = await _familyApi.UpdateMyRoleAsync(EditingBabyId, EditingRoleCode);
        if (result is null)
        {
            DisplayToast(_locale.GetString("Family_SaveFailed", "保存失败"));
            return;
        }
        IsRoleEditorOpen = false;
        DisplayToast(string.Format(_locale.GetString("Family_RoleUpdated", "角色已更新为：{0}"), FamilyRoles.GetRoleName(EditingRoleCode)));
        await RefreshFamiliesAsync();
    }

    // ==================== 家人管理：owner 移除家庭成员 ====================

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
        var ok = await _familyApi.RemoveMemberAsync(_removeTargetBabyId, _removeTargetUserId);
        if (!ok)
        {
            DisplayToast(_locale.GetString("Family_RemoveFailed", "移除失败，请稀后再试"));
            return;
        }
        IsRemoveConfirmOpen = false;
        DisplayToast(string.Format(_locale.GetString("Family_RemovedToast", "已移除「{0}」"), _removeTargetNickName));
        await RefreshFamiliesAsync();
    }

    // ==================== 家人管理：owner 审批加入申请 ====================

    [RelayCommand]
    private async Task ApproveRequest(JoinRequestItem? req)
    {
        if (req is null) return;
        var result = await _familyApi.ProcessJoinRequestAsync(req.Id, approve: true);
        if (result is null)
        {
            DisplayToast(_locale.GetString("Family_ProcessFailed", "操作失败，请稀后再试"));
            return;
        }
        DisplayToast(string.Format(_locale.GetString("Family_RequestApprovedToast", "已通过「{0}」的申请"), req.ApplicantNickName));
        await RefreshFamiliesAsync();
    }

    [RelayCommand]
    private async Task RejectRequest(JoinRequestItem? req)
    {
        if (req is null) return;
        var result = await _familyApi.ProcessJoinRequestAsync(req.Id, approve: false);
        if (result is null)
        {
            DisplayToast(_locale.GetString("Family_ProcessFailed", "操作失败，请稀后再试"));
            return;
        }
        DisplayToast(string.Format(_locale.GetString("Family_RequestRejectedToast", "已拒绝「{0}」的申请"), req.ApplicantNickName));
        await RefreshFamiliesAsync();
    }

    // ==================== 头像相关方法 ====================

    /// <summary>待保存的头像文件路径（用户选择后暂存，Save 时写入实体）。</summary>
    private string? _pendingAvatarPath;

    /// <summary>清空头像状态（新增模式调用）。</summary>
    private void ClearAvatar()
    {
        AvatarBitmap = null;
        HasAvatar = false;
        _pendingAvatarPath = null;
    }

    /// <summary>从已有路径或 URL 加载头像到 Bitmap（编辑模式调用）。</summary>
    private async void LoadAvatarFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            ClearAvatar();
            return;
        }
        try
        {
            // URL：从服务器下载
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                var bytes = await http.GetByteArrayAsync(path);
                using var ms = new System.IO.MemoryStream(bytes);
                AvatarBitmap = await Task.Run(() => Bitmap.DecodeToWidth(ms, 160));
                HasAvatar = true;
            }
            else if (System.IO.File.Exists(path))
            {
                await using var fs = System.IO.File.OpenRead(path);
                AvatarBitmap = await Task.Run(() => Bitmap.DecodeToWidth(fs, 160));
                HasAvatar = true;
            }
            else
            {
                ClearAvatar();
            }
        }
        catch (Exception ex)
        {
            DevLogger.Log("BabyManagerVM", $"加载头像失败: {ex.Message}");
            ClearAvatar();
        }
    }

    /// <summary>从文件选择器结果加载头像图片。先存本地即时显示，再异步上传到服务器存 URL。</summary>
    public async Task LoadAvatarFromFile(IStorageFile file)
    {
        try
        {
            await using var stream = await file.OpenReadAsync();
            // 复制到本地 AppData 目录持久化存储（即时显示 + 上传失败时的回退）
            var avatarDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ChildNotes", "avatars");
            System.IO.Directory.CreateDirectory(avatarDir);

            var ext = System.IO.Path.GetExtension(file.Name)?.ToLowerInvariant() ?? ".jpg";
            if (ext is not (".jpg" or ".jpeg" or ".png" or ".gif" or ".webp"))
                ext = ".jpg";
            var fileName = $"baby_{EditingId}_{DateTime.Now:yyyyMMddHHmmss}{ext}";
            var localPath = System.IO.Path.Combine(avatarDir, fileName);

            // 从选择器流复制到本地文件
            using var fileStream = System.IO.File.Create(localPath);
            await stream.CopyToAsync(fileStream);

            // 加载为 Bitmap 显示
            fileStream.Position = 0;  // 重置位置后重新读取
            AvatarBitmap = await Task.Run(() => Bitmap.DecodeToWidth(fileStream, 160));
            HasAvatar = true;

            // 先用本地路径，保证即使上传失败也能保存（旧设备仍可用本地路径）
            _pendingAvatarPath = localPath;
            DevLogger.Log("BabyManagerVM", $"头像已选择: {localPath}");

            // 异步上传到服务器，成功则改存 URL（跨设备可访问）
            var uploadService = ServiceProvider.Instance.UploadService;
            var serverUrl = await uploadService.UploadToServerAsync(localPath);
            if (!string.IsNullOrEmpty(serverUrl))
            {
                _pendingAvatarPath = serverUrl;
                DevLogger.Log("BabyManagerVM", $"头像已上传: {serverUrl}");
            }
            else
            {
                DevLogger.Log("BabyManagerVM", "头像上传失败，保留本地路径");
            }
        }
        catch (Exception ex)
        {
            DevLogger.Log("BabyManagerVM", $"加载头像失败: {ex.Message}");
        }
    }
}
