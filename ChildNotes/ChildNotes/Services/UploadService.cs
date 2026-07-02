using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using Avalonia.Platform.Storage;
using ChildNotes.Data.Repositories;
using ChildNotes.Infrastructure;

namespace ChildNotes.Services;

/// <summary>
/// 图片存储与上传服务。
/// 本地模式：SaveImageAsync / SaveLocalImage 把图片复制到 LocalApplicationData/ChildNotes/images/，
///           返回本地绝对路径，立即可用于 UI 显示。
/// 异步上传：UploadToServerAsync 把本地图片 POST 到后端 /api/upload，返回服务器 URL。
///           失败不抛异常（返回 null），调用方可保留本地路径继续使用，下次同步时重试。
/// </summary>
public sealed class UploadService
{
    private readonly string _storageRoot;
    private readonly SyncConfigRepository? _cfgRepo;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    public UploadService(string storageRoot)
    {
        _storageRoot = storageRoot;
        Directory.CreateDirectory(_storageRoot);
    }

    /// <summary>带同步配置的构造函数，启用 UploadToServerAsync 能力。</summary>
    public UploadService(string storageRoot, SyncConfigRepository cfgRepo) : this(storageRoot)
    {
        _cfgRepo = cfgRepo;
    }

    public async Task<string?> SaveImageAsync(IStorageFile file)
    {
        var ext = NormalizeExt(Path.GetExtension(file.Name));
        var fileName = $"img_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(_storageRoot, fileName);
        await using var stream = await file.OpenReadAsync();
        await using var fs = File.Create(fullPath);
        await stream.CopyToAsync(fs);
        return fullPath;
    }

    public string? SaveLocalImage(string sourcePath)
    {
        if (!File.Exists(sourcePath)) return null;
        var ext = NormalizeExt(Path.GetExtension(sourcePath));
        var fileName = $"img_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(_storageRoot, fileName);
        File.Copy(sourcePath, fullPath, true);
        return fullPath;
    }

    /// <summary>
    /// 异步上传本地图片到后端 /api/upload。成功返回服务器 URL，失败返回 null（不抛异常）。
    /// 需要 _cfgRepo 已注入；未配置同步或无 token 时返回 null。
    /// </summary>
    public async Task<string?> UploadToServerAsync(string localPath, CancellationToken ct = default)
    {
        if (_cfgRepo is null) return null;
        if (!File.Exists(localPath)) return null;

        var cfg = _cfgRepo.Get();
        var serverUrl = string.IsNullOrWhiteSpace(cfg.ServerUrl) ? ServerEndpoints.Primary : cfg.ServerUrl;
        if (string.IsNullOrWhiteSpace(cfg.Token)) return null;

        var url = serverUrl.TrimEnd('/') + "/api/upload";
        using var form = new MultipartFormDataContent();
        await using var fs = File.OpenRead(localPath);
        var fileContent = new StreamContent(fs);
        var ext = NormalizeExt(Path.GetExtension(localPath));
        var contentType = ext.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "image/jpeg",
        };
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        var fileName = Path.GetFileName(localPath);
        form.Add(fileContent, "file", fileName);

        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.Token);
        try
        {
            using var resp = await Http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                DevLogger.Log("Upload", $"UploadToServer fail: {(int)resp.StatusCode}");
                return null;
            }
            var json = await resp.Content.ReadAsStringAsync(ct);
            // 后端响应信封：{ state, msg, data: { url } }
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("url", out var urlEl))
            {
                return urlEl.GetString();
            }
            DevLogger.Log("Upload", "Upload response missing data.url");
            return null;
        }
        catch (Exception ex)
        {
            DevLogger.Log("Upload", "UploadToServer ex: " + ex.Message);
            return null;
        }
    }

    private static string NormalizeExt(string? ext)
    {
        if (string.IsNullOrEmpty(ext)) return ".jpg";
        return ext.ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" ? ext.ToLowerInvariant() : ".jpg";
    }
}
