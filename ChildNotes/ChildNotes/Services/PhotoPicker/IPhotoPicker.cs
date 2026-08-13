using Avalonia.Platform.Storage;

namespace ChildNotes.Services.PhotoPicker;

/// <summary>
/// 跨平台图片选择器抽象。
/// Android 实现使用系统 Photo Picker（AndroidX Activity ResultContracts.PickVisualMedia，
/// Android 13+ 原生相册网格，13- 自动回退 ACTION_OPEN_DOCUMENT），无需任何运行时权限。
/// 桌面端默认实现仍使用 Avalonia StorageProvider.OpenFilePickerAsync（Win32 文件对话框）。
/// </summary>
/// <remarks>
/// 返回的是已复制到 App 私有目录的本地文件绝对路径（不是 content:// URI 也不是 IStorageFile），
/// 调用方可直接传给 UploadService 压缩/上传，或用 Bitmap.DecodeToWidth 加载显示。
/// 用户取消时返回 null。
/// </remarks>
public interface IPhotoPicker
{
    /// <summary>
    /// 选择单张图片，返回已落地的本地路径，取消返回 null。
    /// </summary>
    Task<string?> PickImageAsync();

    /// <summary>
    /// 选择多张图片（上限 maxCount），返回已落地的本地路径列表，取消或未选返回空列表。
    /// </summary>
    Task<List<string>> PickImagesAsync(int maxCount);
}

/// <summary>
/// 默认桌面端实现：仍使用 Avalonia StorageProvider（Win32 文件对话框 / macOS NSOpenPanel）。
/// Android 平台启动时会通过 ServiceProvider.OverridePhotoPicker 注入 AndroidPhotoPicker 覆盖此实现。
///
/// 设计说明：桌面端不是正式发布平台，仅用于开发调试，保留系统文件选择器行为即可。
/// 将 IStorageFile 结果复制到本地 images 目录再返回路径，与 Android 实现行为对齐，
/// 让上层 UploadService / ViewModel 不需关心平台差异。
/// </summary>
internal sealed class DesktopPhotoPicker : IPhotoPicker
{
    private readonly Func<Avalonia.Controls.TopLevel?> _topLevelAccessor;

    /// <param name="topLevelAccessor">延迟获取 TopLevel：调用时从当前控件取，避免构造时主窗口未就绪。</param>
    public DesktopPhotoPicker(Func<Avalonia.Controls.TopLevel?> topLevelAccessor)
    {
        _topLevelAccessor = topLevelAccessor;
    }

    public async Task<string?> PickImageAsync()
    {
        var provider = GetStorageProvider();
        if (provider is null) return null;

        var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = GetImageFileTypeFilter(),
        });

        if (files.Count == 0) return null;
        return await CopyToLocalStorage(files[0]);
    }

    public async Task<List<string>> PickImagesAsync(int maxCount)
    {
        var provider = GetStorageProvider();
        if (provider is null) return new List<string>();

        var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            FileTypeFilter = GetImageFileTypeFilter(),
        });

        var results = new List<string>(files.Count);
        foreach (var file in files)
        {
            if (results.Count >= maxCount) break;
            var path = await CopyToLocalStorage(file);
            if (path is not null) results.Add(path);
        }
        return results;
    }

    private Avalonia.Platform.Storage.IStorageProvider? GetStorageProvider()
    {
        var topLevel = _topLevelAccessor();
        return topLevel?.StorageProvider;
    }

    private static IReadOnlyList<FilePickerFileType> GetImageFileTypeFilter() =>
        new[]
        {
            new FilePickerFileType("图片文件")
            {
                Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.gif", "*.webp" },
                MimeTypes = new[] { "image/jpeg", "image/png", "image/gif", "image/webp" },
            },
        };

    /// <summary>
    /// 把 IStorageFile 复制到 LocalApplicationData/ChildNotes/images/picked/ 目录，返回本地绝对路径。
    /// 与 Android 实现行为对齐：调用方拿到的总是真实文件系统路径而非 URI。
    /// </summary>
    private static async Task<string?> CopyToLocalStorage(Avalonia.Platform.Storage.IStorageFile file)
    {
        try
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ChildNotes", "images", "picked");
            System.IO.Directory.CreateDirectory(dir);

            var ext = System.IO.Path.GetExtension(file.Name)?.ToLowerInvariant() ?? ".jpg";
            if (ext is not (".jpg" or ".jpeg" or ".png" or ".gif" or ".webp")) ext = ".jpg";
            var fileName = $"picked_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid():N}{ext}";
            var fullPath = System.IO.Path.Combine(dir, fileName);

            await using var src = await file.OpenReadAsync();
            using var dst = System.IO.File.Create(fullPath);
            await src.CopyToAsync(dst);
            return fullPath;
        }
        catch (Exception ex)
        {
            Infrastructure.DevLogger.Log("PhotoPicker", $"Desktop 复制文件失败: {ex.Message}");
            return null;
        }
    }
}
