using System.Collections.ObjectModel;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

public partial class BabyManagerViewModel : ViewModelBase
{
    private readonly BabyService _babyService = ServiceProvider.Instance.BabyService;
    private readonly AppState _state = ServiceProvider.Instance.AppState;
    private readonly LocaleManager _locale = LocaleManager.Instance;

    public ObservableCollection<Baby> BabyList { get; } = new();

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

    /// <summary>无头像时显示的性别 emoji 占位符。</summary>
    public string GenderEmoji => Gender switch
    {
        "girl" => "\U0001F467",  // 👧
        _ => "\U0001F466",        // 👦
    };

    public event Action? BabyChanged;        // 增删改后通知外部刷新

    public BabyManagerViewModel()
    {
        _locale.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(AppLanguage lang)
    {
        // 编辑器打开时刷新标题文案；关闭时由 OpenAdd/OpenEdit 重新设置
        if (IsEditorOpen)
            EditorTitle = IsEditing
                ? _locale.GetString("BabyMgr_EditTitle", "编辑宝宝")
                : _locale.GetString("BabyMgr_AddTitle", "添加宝宝");
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
    /// 用于弹层"先打开再加载"模式，避免阻塞 UI。
    /// </summary>
    public async Task LoadAsync()
    {
        var list = await Task.Run(() => _babyService.LoadBabyList());
        BabyList.Clear();
        foreach (var b in list) BabyList.Add(b);
        HasBaby = list.Count > 0;
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
    /// 宝宝主人可发送给家人，家人凭此 ID 在「家人管理」中加入家庭。
    /// </summary>
    public async Task CopyEditingIdAsync()
    {
        var id = EditingId;
        if (string.IsNullOrWhiteSpace(id))
        {
            DisplayToast(_locale.GetString("BabyMgr_IdEmpty", "宝宝 ID 为空"));
            return;
        }
        var clipboard = ServiceProvider.Instance.MainView?.Clipboard;
        if (clipboard is null)
        {
            DisplayToast(_locale.GetString("BabyMgr_ClipUnavailable", "剪贴板不可用"));
            return;
        }
        await clipboard.SetTextAsync(id);
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
