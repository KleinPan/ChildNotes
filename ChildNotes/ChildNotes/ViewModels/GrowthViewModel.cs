using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChildNotes.Data.Repositories;
using ChildNotes.Infrastructure;
using ChildNotes.Models;

namespace ChildNotes.ViewModels;

public partial class GrowthViewModel : ViewModelBase, IActivatable
{
    private readonly MilestoneRepository _milestoneRepo = ServiceProvider.Instance.MilestoneRepository;

    public ObservableCollection<MilestoneDisplayItem> Milestones { get; } = new();
    public MilestoneEditViewModel MilestoneEdit { get; } = new();

    /// <summary>当前预览的图片路径列表（本地路径或远程 URL）。</summary>
    public List<string> PreviewPhotos { get; private set; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewCountText))]
    [NotifyPropertyChangedFor(nameof(CanPreviewPrev))]
    [NotifyPropertyChangedFor(nameof(CanPreviewNext))]
    private int _previewIndex;

    [ObservableProperty]
    private Bitmap? _previewCurrentBitmap;

    [ObservableProperty]
    private bool _isPreviewOpen;

    public string PreviewCountText => PreviewPhotos.Count > 0 ? $"{PreviewIndex + 1} / {PreviewPhotos.Count}" : string.Empty;
    public bool CanPreviewPrev => PreviewIndex > 0;
    public bool CanPreviewNext => PreviewIndex < PreviewPhotos.Count - 1;

    public GrowthViewModel()
    {
        MilestoneEdit.Saved += OnMilestoneSaved;
    }

    public void Activate()
    {
        _ = LoadDataAsync();
    }

    /// <summary>
    /// 异步加载数据：DB 查询移到后台线程，避免切换 tab 时阻塞 UI。
    /// </summary>
    private async Task LoadDataAsync()
    {
        var state = ServiceProvider.Instance.AppState;
        var babyId = state.CurrentBabyId;
        var userId = state.UserId;
        // 后台线程执行 DB 查询
        var list = await Task.Run(() => _milestoneRepo.GetAll(userId, babyId)
                                                  .OrderBy(x => x.RecordDate)
                                                  .ThenBy(x => x.Id)
                                                  .ToList());
        Milestones.Clear();
        foreach (var m in list)
        {
            Milestones.Add(new MilestoneDisplayItem(m));
        }
    }

    [RelayCommand]
    private void AddMilestone()
    {
        MilestoneEdit.InitForAdd();
        MilestoneEdit.Show();
    }

    public void EditMilestone(MilestoneDisplayItem item)
    {
        MilestoneEdit.InitForEdit(item.Milestone);
        MilestoneEdit.Show();
    }

    public void DeleteMilestone(MilestoneDisplayItem item)
    {
        _milestoneRepo.Delete(item.Milestone.Id);
        _ = LoadDataAsync();
    }

    public void OnMilestoneSaved()
    {
        _ = LoadDataAsync();
    }

    /// <summary>
    /// 打开大图预览：由 View 点击缩略图时调用。
    /// index 为点击的缩略图索引，photos 为该条记录的全部图片路径。
    /// </summary>
    public void OpenPreview(IReadOnlyList<string> photos, int index)
    {
        if (photos.Count == 0) return;
        PreviewPhotos = photos.ToList();
        PreviewIndex = Math.Clamp(index, 0, photos.Count - 1);
        IsPreviewOpen = true;
        _ = LoadPreviewBitmapAsync();
    }

    [RelayCommand]
    private void ClosePreview()
    {
        IsPreviewOpen = false;
        PreviewCurrentBitmap = null;
    }

    [RelayCommand(CanExecute = nameof(CanPreviewPrev))]
    private void PreviewPrev()
    {
        if (PreviewIndex <= 0) return;
        PreviewIndex--;
        _ = LoadPreviewBitmapAsync();
    }

    [RelayCommand(CanExecute = nameof(CanPreviewNext))]
    private void PreviewNext()
    {
        if (PreviewIndex >= PreviewPhotos.Count - 1) return;
        PreviewIndex++;
        _ = LoadPreviewBitmapAsync();
    }

    /// <summary>加载当前索引的大图（本地原图或远程原图，不走缩略图缓存）。</summary>
    private async Task LoadPreviewBitmapAsync()
    {
        if (PreviewIndex < 0 || PreviewIndex >= PreviewPhotos.Count) return;
        var path = PreviewPhotos[PreviewIndex];
        PreviewCurrentBitmap = null;
        try
        {
            if (!path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                if (!File.Exists(path)) return;
                using var fs = File.OpenRead(path);
                // 大图：解码到较大宽度，避免内存爆炸（最大边约 1920）
                PreviewCurrentBitmap = await Task.Run(() => Bitmap.DecodeToWidth(fs, 1920));
            }
            else
            {
                // 远程图：直接下载原图（已是压缩后的同步图，无需再降采样）
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                using var resp = await http.GetAsync(path, HttpCompletionOption.ResponseHeadersRead);
                if (!resp.IsSuccessStatusCode) return;
                await using var stream = await resp.Content.ReadAsStreamAsync();
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                ms.Position = 0;
                PreviewCurrentBitmap = await Task.Run(() => new Bitmap(ms));
            }
        }
        catch (Exception ex)
        {
            DevLogger.Log("GrowthPreview", $"大图加载失败: {ex.Message}");
        }
    }

    partial void OnPreviewIndexChanged(int value)
    {
        PreviewPrevCommand.NotifyCanExecuteChanged();
        PreviewNextCommand.NotifyCanExecuteChanged();
    }
}

public sealed class MilestoneDisplayItem
{
    public Milestone Milestone { get; }
    public string DateText => ServiceProvider.Instance.DateTimeFormatter.FormatDate(Milestone.RecordDate);
    public string Title => Milestone.Title;
    public string? Content => Milestone.Content;
    public List<string> Photos => string.IsNullOrEmpty(Milestone.PhotosJson)
        ? new()
        : JsonSerializer.Deserialize<List<string>>(Milestone.PhotosJson) ?? new();
    public bool HasPhotos => Photos.Count > 0;
    /// <summary>卡片缩略图最多展示 4 张（与表单上限对齐）。远程 URL 异步加载，本地路径同步加载。</summary>
    public List<MilestoneThumbItem> PhotoThumbnails { get; }

    public MilestoneDisplayItem(Milestone m)
    {
        Milestone = m;
        PhotoThumbnails = Photos.Take(4).Select(p => new MilestoneThumbItem(p)).ToList();
    }
}

/// <summary>
/// 成长记录卡片缩略图视图项。
/// 本地路径：构造时立即同步加载 Bitmap。
/// 远程 URL：构造时启动后台下载（带进程内缓存），下载完成后通知 UI 更新。
/// </summary>
public sealed class MilestoneThumbItem : ObservableObject
{
    private Bitmap? _bitmap;
    public Bitmap? Bitmap
    {
        get => _bitmap;
        private set => SetProperty(ref _bitmap, value);
    }

    public string Source { get; }

    public MilestoneThumbItem(string source)
    {
        Source = source;
        _ = LoadAsync(this);
    }

    private static async Task LoadAsync(MilestoneThumbItem item)
    {
        var path = item.Source;
        if (string.IsNullOrWhiteSpace(path)) return;

        // 本地路径：同步存在性检查 + 后台解码
        if (!path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            if (!File.Exists(path)) return;
            try
            {
                using var fs = File.OpenRead(path);
                item.Bitmap = await Task.Run(() => Bitmap.DecodeToWidth(fs, 200));
            }
            catch (Exception ex)
            {
                DevLogger.Log("GrowthThumb", $"本地图片加载失败: {ex.Message}");
            }
            return;
        }

        // 远程 URL：带进程内缓存，避免列表滚动重复下载
        if (RemoteThumbCache.TryGet(path, out var cached))
        {
            item.Bitmap = cached;
            return;
        }
        try
        {
            var bmp = await RemoteThumbCache.LoadAsync(path);
            if (bmp is not null) item.Bitmap = bmp;
        }
        catch (Exception ex)
        {
            DevLogger.Log("GrowthThumb", $"远程图片加载失败: {ex.Message}");
        }
    }
}

/// <summary>远程缩略图进程内缓存：同 URL 只下载一次，避免列表滚动/重建重复请求。</summary>
internal static class RemoteThumbCache
{
    private static readonly ConcurrentDictionary<string, Bitmap?> _cache = new();
    private static readonly ConcurrentDictionary<string, Task<Bitmap?>> _loading = new();
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static bool TryGet(string url, out Bitmap? bmp) => _cache.TryGetValue(url, out bmp);

    public static async Task<Bitmap?> LoadAsync(string url)
    {
        if (_cache.TryGetValue(url, out var cached)) return cached;
        // 同 URL 并发请求合并为一次下载
        var task = _loading.GetOrAdd(url, async u =>
        {
            try
            {
                using var resp = await Http.GetAsync(u, HttpCompletionOption.ResponseHeadersRead);
                if (!resp.IsSuccessStatusCode)
                {
                    DevLogger.Log("GrowthThumb", $"下载失败: {(int)resp.StatusCode}");
                    return null;
                }
                await using var stream = await resp.Content.ReadAsStreamAsync();
                // 先读到内存流再解码，避免网络流和 Bitmap 跨线程访问冲突
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                ms.Position = 0;
                var bmp = await Task.Run(() => Bitmap.DecodeToWidth(ms, 200));
                _cache.TryAdd(u, bmp);
                return bmp;
            }
            finally
            {
                _loading.TryRemove(u, out _);
            }
        });
        return await task;
    }
}
