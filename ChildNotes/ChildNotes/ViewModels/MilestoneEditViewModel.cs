using System.Collections.ObjectModel;
using System.Text.Json;
using System.IO;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Data.Repositories;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Services;

namespace ChildNotes.ViewModels;

/// <summary>
/// 单张待保存照片的视图项。
/// <list type="bullet">
///   <item><description>Source: 显示用路径（本地路径或远程 URL，UI 据此加载 Bitmap）</description></item>
///   <item><description>LocalPath: 本地持久化路径；新增时与 Source 相同；编辑加载远程 URL 时为 null</description></item>
///   <item><description>RemoteUrl: 异步上传成功后的服务器 URL；尚未上传或上传失败为 null</description></item>
/// </list>
/// 序列化到 PhotosJson 时优先用 RemoteUrl，回退 Source（Source 在新增时为本地路径，
/// 编辑加载远程 URL 时为远程 URL，因此 LocalPath 为 null 时仍可正确序列化）。
/// </summary>
public sealed class MilestonePhotoItem : ObservableObject
{
    public string Source { get; }
    public string? LocalPath { get; }
    public string? RemoteUrl { get; private set; }

    private Bitmap? _bitmap;
    public Bitmap? Bitmap
    {
        get => _bitmap;
        set => SetProperty(ref _bitmap, value);
    }

    private bool _isUploading;
    public bool IsUploading
    {
        get => _isUploading;
        set => SetProperty(ref _isUploading, value);
    }

    public MilestonePhotoItem(string source, string? localPath, string? remoteUrl = null)
    {
        Source = source;
        LocalPath = localPath;
        RemoteUrl = remoteUrl;
    }

    /// <summary>标记异步上传已完成（成功则更新 RemoteUrl）。</summary>
    public void MarkUploaded(string? url)
    {
        RemoteUrl = url;
        IsUploading = false;
    }

    /// <summary>序列化时使用的最终路径：优先远程 URL，回退本地路径。</summary>
    public string ToStoredPath() => !string.IsNullOrWhiteSpace(RemoteUrl) ? RemoteUrl : Source;
}

public partial class MilestoneEditViewModel : ViewModelBase
{
    private readonly MilestoneRepository _repo = ServiceProvider.Instance.MilestoneRepository;
    private readonly AppState _state = ServiceProvider.Instance.AppState;
    private readonly UploadService _upload = ServiceProvider.Instance.UploadService;
    private readonly SyncConfigRepository _cfgRepo = ServiceProvider.Instance.SyncConfigRepository;
    private readonly LocaleManager _locale = LocaleManager.Instance;

    private const int MaxPhotos = 4;

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _content = string.Empty;
    [ObservableProperty] private DateTimeOffset _recordDate = DateTimeOffset.Now;
    [ObservableProperty] private string _sheetTitle = string.Empty;
    [ObservableProperty] private bool _isVisible;

    public MilestoneEditViewModel()
    {
        _sheetTitle = _locale.GetString("Growth_AddTitle", "添加成长时刻");
        _locale.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(AppLanguage lang)
    {
        // 仅在编辑器打开时刷新标题文案，避免后台实例无谓刷新
        if (!IsVisible) return;
        SheetTitle = string.IsNullOrEmpty(_editingId)
            ? _locale.GetString("Growth_AddTitle", "添加成长时刻")
            : _locale.GetString("Growth_EditAdd", "编辑成长时刻");
    }

    /// <summary>表单内编辑中的照片列表。</summary>
    public ObservableCollection<MilestonePhotoItem> Photos { get; } = new();

    /// <summary>是否还能继续添加照片（达到上限时隐藏"+"按钮）。</summary>
    public bool CanAddPhoto => Photos.Count < MaxPhotos;

    private string _editingId = string.Empty;

    public event Action? Saved;

    public void InitForAdd()
    {
        _editingId = string.Empty;
        Title = string.Empty;
        Content = string.Empty;
        var localDate = DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Local);
        RecordDate = new DateTimeOffset(localDate);
        SheetTitle = "添加成长时刻";
        ErrorMessage = string.Empty;
        Photos.Clear();
        RefreshCanAddPhoto();
    }

    public void InitForEdit(Milestone m)
    {
        _editingId = m.Id;
        Title = m.Title;
        Content = m.Content ?? string.Empty;
        var localDate = DateTime.SpecifyKind(m.RecordDate.Date, DateTimeKind.Local);
        RecordDate = new DateTimeOffset(localDate);
        SheetTitle = "编辑成长时刻";
        ErrorMessage = string.Empty;
        Photos.Clear();
        foreach (var path in DeserializePhotos(m.PhotosJson))
        {
            var item = new MilestonePhotoItem(path, localPath: IsLocalPath(path) ? path : null, remoteUrl: IsRemoteUrl(path) ? path : null);
            LoadBitmapAsync(item);
            Photos.Add(item);
        }
        RefreshCanAddPhoto();
    }

    public void Show() => IsVisible = true;

    [RelayCommand]
    private void Close()
    {
        IsVisible = false;
    }

    /// <summary>
    /// 添加图片：由 View 调用，传入已落地的本地图片路径（由 IPhotoPicker 返回）。
    /// 流程：1) 压缩为缩略图存本地 images 目录（按会员等级选参数，原图不保留）；
    ///       2) 加入 Photos 集合立即显示；
    ///       3) 后台异步上传压缩图到服务器，成功后用 URL 替换 Source（PhotosJson 序列化为 URL）。
    /// </summary>
    public async Task AddPhotoAsync(string sourcePath)
    {
        if (Photos.Count >= MaxPhotos) return;

        // 1. 压缩为缩略图并保存到本地持久化目录
        var localPath = await _upload.CompressAndSaveFromPathAsync(sourcePath);
        if (localPath is null)
        {
            ErrorMessage = _locale.GetString("Growth_ErrPhotoSave", "图片保存失败");
            return;
        }

        // 2. 加入集合立即显示
        var item = new MilestonePhotoItem(localPath, localPath: localPath);
        LoadBitmapAsync(item);
        Photos.Add(item);
        RefreshCanAddPhoto();

        // 3. 后台异步上传到服务器（不阻塞 UI，失败静默保留本地路径）
        item.IsUploading = true;
        _ = UploadPhotoAsync(item);
    }

    /// <summary>
    /// 批量添加多张图片：循环调用单张逻辑，达到上限时自动停止。
    /// 供 Photo Picker 多选场景使用。
    /// </summary>
    public async Task AddPhotosAsync(IReadOnlyList<string> sourcePaths)
    {
        foreach (var path in sourcePaths)
        {
            if (Photos.Count >= MaxPhotos) break;
            await AddPhotoAsync(path);
        }
    }

    private async Task UploadPhotoAsync(MilestonePhotoItem item)
    {
        try
        {
            // 未配置同步或无 token 时跳过上传，保留本地路径
            var url = await _upload.UploadToServerAsync(item.LocalPath ?? item.Source);
            item.MarkUploaded(url);
            if (url is null)
            {
                DevLogger.Log("MilestoneEdit", "图片异步上传未完成（未配置同步或失败），保留本地路径");
            }
        }
        catch (Exception ex)
        {
            DevLogger.Log("MilestoneEdit", "图片异步上传异常: " + ex.Message);
            item.MarkUploaded(null);
        }
    }

    [RelayCommand]
    private void RemovePhoto(MilestonePhotoItem? item)
    {
        if (item is null) return;
        Photos.Remove(item);
        RefreshCanAddPhoto();
        // 注：与小程序一致，删除仅从本地数组移除，不通知后端清理已上传文件（可能产生孤儿文件）
    }

    private void RefreshCanAddPhoto() => OnPropertyChanged(nameof(CanAddPhoto));

    /// <summary>异步加载本地图片为 Bitmap（后台线程解码，避免阻塞 UI）。</summary>
    private static async void LoadBitmapAsync(MilestonePhotoItem item)
    {
        var path = item.Source;
        try
        {
            // 远程 URL 不在此加载（UI 层用 Image 异步加载或显示占位）
            if (!File.Exists(path)) return;
            await using var fs = File.OpenRead(path);
            item.Bitmap = await Task.Run(() => new Bitmap(fs));
        }
        catch (Exception ex)
        {
            DevLogger.Log("MilestoneEdit", $"加载图片失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task Save()
    {
        ErrorMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(Title))
        {
            ErrorMessage = "请输入标题";
            return;
        }
        if (RecordDate.LocalDateTime.Date > DateTime.Today)
        {
            ErrorMessage = "日期不能晚于今天";
            return;
        }

        // 保存逻辑（含等待图片上传 + DB 写入）移到后台线程
        await Task.Run(() =>
        {
            // 等待图片上传完成：弱网下 5 秒太短会回退本地路径（跨设备不可达），提高到 30 秒
            WaitForUploads(TimeSpan.FromSeconds(30));

            var photos = Photos.Select(p => p.ToStoredPath()).ToList();
            var m = new Milestone
            {
                Id = _editingId,
                UserId = _state.GetLocalDataSpaceId(),
                BabyId = _state.CurrentBabyId,
                Title = Title.Trim(),
                Content = string.IsNullOrWhiteSpace(Content) ? null : Content.Trim(),
                RecordDate = RecordDate.LocalDateTime.Date,
                PhotosJson = JsonSerializer.Serialize(photos),
                DeviceId = _cfgRepo.Get().DeviceId,
            };

            if (string.IsNullOrEmpty(_editingId))
                _repo.Insert(m);
            else
                _repo.Update(m);
        });

        IsVisible = false;
        Saved?.Invoke();
    }

    /// <summary>等待所有 IsUploading=true 的图片完成上传。超时后强制结束。</summary>
    private void WaitForUploads(TimeSpan timeout)
    {
        var deadline = DateTime.Now + timeout;
        while (DateTime.Now < deadline && Photos.Any(p => p.IsUploading))
        {
            Thread.Sleep(100);
        }
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (string.IsNullOrEmpty(_editingId)) return;
        await Task.Run(() => _repo.Delete(_editingId));
        IsVisible = false;
        Saved?.Invoke();
    }

    private static List<string> DeserializePhotos(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new(); }
        catch { return new(); }
    }

    private static bool IsLocalPath(string s) => !string.IsNullOrWhiteSpace(s) && !s.StartsWith("http", StringComparison.OrdinalIgnoreCase);
    private static bool IsRemoteUrl(string s) => s.StartsWith("http", StringComparison.OrdinalIgnoreCase);
}
