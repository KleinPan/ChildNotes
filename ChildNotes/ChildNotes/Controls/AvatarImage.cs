using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ChildNotes.Services;

namespace ChildNotes.Controls;

/// <summary>
/// 头像图片控件：接收本地路径或 URL，异步加载 Bitmap。
/// 替代 AvatarPathToBitmapConverter 在 Converter 内同步阻塞 HTTP 的做法（会导致 UI 线程卡死）。
/// 加载在后台线程执行，完成后通过 Dispatcher.UIThread.Post 回 UI 线程赋值 Source。
/// 内置 URL→Bitmap 内存缓存，避免列表滚动重复下载。
/// </summary>
public sealed class AvatarImage : Image
{
    /// <summary>头像路径（本地文件路径或 http/https URL）。空/null 时清空显示。</summary>
    public static readonly StyledProperty<string?> PathProperty =
        AvaloniaProperty.Register<AvatarImage, string?>(nameof(Path));

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>URL → Bitmap 缓存，避免列表滚动和页面切换重复下载。</summary>
    private static readonly ConcurrentDictionary<string, Bitmap> Cache = new();

    private int _loadVersion; // 防止旧加载覆盖新赋值

    static AvatarImage()
    {
        // Path 变化时触发重新加载
        PathProperty.Changed.AddClassHandler<AvatarImage>((img, e) => img.OnPathChanged(e));
    }

    public string? Path
    {
        get => GetValue(PathProperty);
        set => SetValue(PathProperty, value);
    }

    private void OnPathChanged(AvaloniaPropertyChangedEventArgs e)
    {
        var newPath = (string?)e.NewValue;
        if (string.IsNullOrWhiteSpace(newPath))
        {
            _loadVersion++;
            Source = null;
            return;
        }

        // 本地文件：可直接同步读取（<5ms，但仍在后台线程以保持一致）
        // URL：必须异步下载
        LoadAsync(newPath);
    }

    private async void LoadAsync(string path)
    {
        var version = ++_loadVersion;

        Bitmap? bmp = null;
        try
        {
            // 服务器相对路径（如 /uploads/2024/01/01/xxx.jpg）：拼接当前主服务器地址后走 HTTP 下载。
            // 后端 UploadService 返回的是相对路径，跨设备同步后本地无此文件，必须拼成完整 URL 才能加载。
            if (path.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            {
                path = ServerEndpoints.Primary.TrimEnd('/') + path;
            }

            // URL：真 async HTTP 下载（不占用 ThreadPool 线程等待）+ 后台 CPU 解码
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                // 命中缓存则直接用
                if (Cache.TryGetValue(path, out var cached))
                {
                    bmp = cached;
                }
                else
                {
                    // HTTP 用真 async：I/O 等待期间不占用任何线程
                    var bytes = await Http.GetByteArrayAsync(path);
                    // Bitmap 解码是 CPU 密集型，用 Task.Run 包裹
                    bmp = await Task.Run(() =>
                    {
                        using var ms = new MemoryStream(bytes);
                        return Bitmap.DecodeToWidth(ms, 160);
                    });
                    if (bmp is not null)
                        Cache[path] = bmp;
                }
            }
            else
            {
                // 本地文件路径：文件读取+解码都是 CPU/IO，用 Task.Run 包裹
                bmp = await Task.Run(() =>
                {
                    if (!File.Exists(path))
                        return null;
                    try
                    {
                        using var fs = File.OpenRead(path);
                        return Bitmap.DecodeToWidth(fs, 160);
                    }
                    catch
                    {
                        return null;
                    }
                });
            }
        }
        catch
        {
            bmp = null;
        }

        // 加载期间 Path 已被改写：丢弃本次结果（防旧数据覆盖新 UI）
        if (version != _loadVersion)
            return;

        // 回 UI 线程赋值
        Dispatcher.UIThread.Post(() =>
        {
            if (version != _loadVersion)
                return;
            Source = bmp;
        });
    }

    /// <summary>清空 URL→Bitmap 缓存（内存紧张或切换宝宝时调用）。</summary>
    public static void ClearCache()
    {
        foreach (var kv in Cache)
        {
            try { kv.Value.Dispose(); } catch { }
        }
        Cache.Clear();
    }
}
