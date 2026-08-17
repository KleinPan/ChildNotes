using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using ChildNotes.Data.Repositories;
using ChildNotes.Infrastructure;
using ChildNotes.Shared.Constants;

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
    /// 将源图片压缩为同步用缩略图并保存到本地 images/ 目录，返回缩略图本地路径。
    /// 压缩参数按当前用户会员状态选择：会员高质量（1920px/92%），普通用户（1280px/85%）。
    /// 失败时返回 null，调用方可回退到 SaveImageAsync 保留原图。
    /// </summary>
    public async Task<string?> CompressAndSaveAsync(IStorageFile file)
    {
        try
        {
            await using var stream = await file.OpenReadAsync();
            using var srcBmp = new Bitmap(stream);
            return CompressAndSave(srcBmp);
        }
        catch (Exception ex)
        {
            DevLogger.Log("Upload", $"CompressAndSaveAsync 失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 从本地文件路径压缩图片并保存到 images/ 目录，返回缩略图本地路径。
    /// 与 CompressAndSaveAsync(IStorageFile) 行为一致，仅入参从 IStorageFile 改为路径，
    /// 供 Android Photo Picker / 桌面端选择器返回的本地路径使用（避免 IStorageFile 依赖）。
    /// 用流式加载而非 Bitmap(string path) 构造函数，确保跨平台行为一致
    /// （Android 上 Bitmap(string) 对某些路径/格式可能解码失败）。
    /// </summary>
    public async Task<string?> CompressAndSaveFromPathAsync(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            DevLogger.Log("Upload", "CompressAndSaveFromPathAsync 失败: sourcePath 为空");
            return null;
        }
        if (!File.Exists(sourcePath))
        {
            DevLogger.Log("Upload", $"CompressAndSaveFromPathAsync 失败: 文件不存在 path={sourcePath}");
            return null;
        }
        try
        {
            var len = new FileInfo(sourcePath).Length;
            DevLogger.Log("Upload", $"CompressAndSaveFromPathAsync start: path={sourcePath}, size={len}");
            var result = await Task.Run(() =>
            {
                // 用 FileStream + Bitmap(stream) 而非 Bitmap(string path)，
                // 与旧 CompressAndSaveAsync(IStorageFile) 路径完全一致，避免平台差异。
                using var fs = File.OpenRead(sourcePath);
                using var srcBmp = new Bitmap(fs);
                return CompressAndSave(srcBmp);
            });
            DevLogger.Log("Upload", $"CompressAndSaveFromPathAsync done: result={(result is null ? "null" : result)}");
            return result;
        }
        catch (Exception ex)
        {
            DevLogger.Log("Upload", $"CompressAndSaveFromPathAsync 异常: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 共用压缩+保存逻辑：把已解码的 Bitmap 按会员参数压缩为 JPEG 存到 images/ 目录。
    /// 注意：调用方负责 srcBmp 的释放；此方法内部创建的 scaled Bitmap 自行释放。
    /// </summary>
    private string? CompressAndSave(Bitmap srcBmp)
    {
        var (maxEdge, quality) = GetCompressParams();
        var scaled = ScaleToMaxEdge(srcBmp, maxEdge);
        try
        {
            var fileName = $"img_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid():N}.jpg";
            var fullPath = Path.Combine(_storageRoot, fileName);
            using var fs = File.Create(fullPath);
            scaled.Save(fs, quality);
            return fullPath;
        }
        finally
        {
            scaled.Dispose();
        }
    }

    /// <summary>按当前用户会员状态返回压缩参数。</summary>
    private static (int maxEdge, int quality) GetCompressParams()
    {
        var user = Infrastructure.ServiceProvider.Instance.AuthService.CurrentUser;
        var isMember = MembershipConstants.IsActive(user?.MembershipExpireAt);
        return isMember
            ? (MembershipConstants.MemberPhotoMaxEdge, MembershipConstants.MemberPhotoJpegQuality)
            : (MembershipConstants.FreePhotoMaxEdge, MembershipConstants.FreePhotoJpegQuality);
    }

    /// <summary>按最大边等比缩放（若原图小于 maxEdge 则不放大）。返回新 Bitmap，调用方负责释放。</summary>
    private static Bitmap ScaleToMaxEdge(Bitmap src, int maxEdge)
    {
        var w = src.PixelSize.Width;
        var h = src.PixelSize.Height;
        var longEdge = Math.Max(w, h);
        if (longEdge <= maxEdge) return src.CreateScaledBitmap(src.PixelSize, BitmapInterpolationMode.HighQuality);
        var scale = (double)maxEdge / longEdge;
        var newW = Math.Max(1, (int)(w * scale));
        var newH = Math.Max(1, (int)(h * scale));
        return src.CreateScaledBitmap(new PixelSize(newW, newH), BitmapInterpolationMode.HighQuality);
    }

    /// <summary>
    /// 异步上传本地图片到后端 /api/upload。成功返回服务器 URL，失败返回 null（不抛异常）。
    /// v5：AccessToken 从 ISecureStorage 读取（非明文 SQLite）；缺失/401 尝试 RefreshToken 续期。
    /// </summary>
    public async Task<string?> UploadToServerAsync(string localPath, CancellationToken ct = default)
    {
        if (_cfgRepo is null) return null;
        if (!File.Exists(localPath)) return null;

        var cfg = _cfgRepo.Get();
        var serverUrl = string.IsNullOrWhiteSpace(cfg.ServerUrl) ? ServerEndpoints.Primary : cfg.ServerUrl;

        // v5：从 SecureStorage 读取 AccessToken；缺失时尝试 Refresh 续期
        var auth = Infrastructure.ServiceProvider.Instance.AuthService;
        var token = await auth.GetAccessTokenAsync(ct);
        if (string.IsNullOrWhiteSpace(token))
        {
            token = await auth.RefreshAccessTokenAsync(ct);
            if (string.IsNullOrEmpty(token))
            {
                DevLogger.Log("Upload", "UploadToServer skip: token 缺失且 Refresh 失败");
                return null;
            }
        }

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
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        try
        {
            using var resp = await Http.SendAsync(req, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                // 401：删除 AccessToken，尝试 Refresh。
                // 不自动重试上传（StreamContent 已被消费，需调用方重新调用 UploadToServerAsync）。
                await auth.InvalidateAccessTokenAsync(ct);
                _ = await auth.RefreshAccessTokenAsync(ct);
                DevLogger.Log("Upload", "UploadToServer 401，已 Refresh token，请稍后重试上传");
                return null;
            }
            if (!resp.IsSuccessStatusCode)
            {
                DevLogger.Log("Upload", $"UploadToServer fail: {(int)resp.StatusCode}");
                return null;
            }
            return await ExtractUrlAsync(resp, ct);
        }
        catch (Exception ex)
        {
            DevLogger.Log("Upload", "UploadToServer ex: " + ex.Message);
            return null;
        }
    }

    private static async Task<string?> ExtractUrlAsync(System.Net.Http.HttpResponseMessage resp, CancellationToken ct)
    {
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

    private static string NormalizeExt(string? ext)
    {
        if (string.IsNullOrEmpty(ext)) return ".jpg";
        return ext.ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" ? ext.ToLowerInvariant() : ".jpg";
    }
}
