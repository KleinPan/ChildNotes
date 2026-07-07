using ChildNotes.Core.Config;
using ChildNotes.Core.Dtos;
using ChildNotes.Core.Exceptions;
using ChildNotes.Core.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace ChildNotes.Infrastructure.Services;

/// <summary>
/// 上传服务：OSS 未配置时用本地文件存储，配置了 OSS 后可扩展。
/// 对齐 Java OssService 的 URL 格式：{baseUrl}/{yyyy/MM/dd}/{32位UUID无横线}{扩展名}
/// </summary>
public class UploadService : IUploadService
{
    private readonly OssOptions _oss;
    private readonly UploadOptions _upload;
    private readonly IWebHostEnvironment _env;

    /// <summary>
    /// 允许的文件扩展名白名单（小写，含点号）。
    /// 仅允许常见图片/视频/PDF 类型，拒绝可执行文件、脚本、HTML 等危险类型。
    /// </summary>
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp",
        ".mp4", ".mov", ".avi", ".mkv",
        ".pdf",
    };

    /// <summary>
    /// 扩展名 -> 期望的 Content-Type 前缀映射（用于二次校验）。
    /// </summary>
    private static readonly Dictionary<string, string> ExtensionContentTypeHint = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/", [".jpeg"] = "image/",
        [".png"] = "image/", [".gif"] = "image/",
        [".webp"] = "image/", [".bmp"] = "image/",
        [".mp4"] = "video/", [".mov"] = "video/",
        [".avi"] = "video/", [".mkv"] = "video/",
        [".pdf"] = "application/pdf",
    };

    /// <summary>
    /// 扩展名 -> 文件魔术字节（前几字节）校验。
    /// 用于防止"伪装扩展名"攻击（如 evil.jpg 实为 HTML/JS）。
    /// </summary>
    private static readonly Dictionary<string, byte[]> ExtensionMagicBytes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = new byte[] { 0xFF, 0xD8, 0xFF },
        [".png"] = new byte[] { 0x89, 0x50, 0x4E, 0x47 },
        [".gif"] = new byte[] { 0x47, 0x49, 0x46, 0x38 },  // GIF8
        [".bmp"] = new byte[] { 0x42, 0x4D },               // BM
        [".pdf"] = new byte[] { 0x25, 0x50, 0x44, 0x46 },   // %PDF
        // webp/mp4/mov/avi/mkv 魔术字节较复杂或在不同位置，省略魔术校验，仅靠扩展名+Content-Type
    };

    public UploadService(IOptions<OssOptions> oss, IOptions<UploadOptions> upload, IWebHostEnvironment env)
    {
        _oss = oss.Value;
        _upload = upload.Value;
        _env = env;
    }

    public async Task<UploadResponse> UploadAsync(Stream stream, string fileName, string? contentType = null, CancellationToken ct = default)
    {
        if (stream.Length > _upload.MaxFileSizeBytes)
            throw new BusinessException($"文件大小超过限制（最大 {_upload.MaxFileSizeBytes / 1024 / 1024}MB）");

        // 安全校验 1：扩展名白名单
        var ext = GetExtension(fileName);
        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
            throw new BusinessException($"不支持的文件类型：{ext}（仅支持图片/视频/PDF）");

        // 安全校验 2：Content-Type 与扩展名一致性（若调用方传入 contentType）
        if (!string.IsNullOrEmpty(contentType)
            && ExtensionContentTypeHint.TryGetValue(ext, out var expectedPrefix)
            && !contentType.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException($"文件 Content-Type ({contentType}) 与扩展名 ({ext}) 不匹配");
        }

        // 安全校验 3：魔术字节校验（针对图片/PDF 等有明确文件头的类型）
        ValidateMagicBytes(stream, ext);

        var datePath = DateTime.UtcNow.ToString("yyyy/MM/dd");
        var uuid = Guid.NewGuid().ToString("N");
        var key = $"{datePath}/{uuid}{ext}";

        string url;
        if (IsOssConfigured())
        {
            url = await UploadToOssAsync(stream, key, ct);
        }
        else
        {
            url = await UploadToLocalAsync(stream, key, ct);
        }
        return new UploadResponse { Url = url };
    }

    /// <summary>
    /// 读取流头部字节，与扩展名对应的魔术字节比对。
    /// 校验后重置流 Position，不影响后续读取。
    /// </summary>
    private static void ValidateMagicBytes(Stream stream, string ext)
    {
        if (!ExtensionMagicBytes.TryGetValue(ext, out var magic) || magic is null || magic.Length == 0)
            return; // 该扩展名无魔术字节校验要求

        if (!stream.CanRead || stream.Length < magic.Length)
            throw new BusinessException("文件内容损坏或过短，无法校验");

        var savedPos = stream.Position;
        stream.Position = 0;
        try
        {
            var buf = new byte[magic.Length];
            var read = stream.Read(buf, 0, magic.Length);
            if (read != magic.Length)
                throw new BusinessException("文件内容读取失败");

            for (var i = 0; i < magic.Length; i++)
            {
                if (buf[i] != magic[i])
                    throw new BusinessException($"文件内容与扩展名 {ext} 不匹配（魔术字节校验失败）");
            }
        }
        finally
        {
            stream.Position = savedPos;
        }
    }

    private bool IsOssConfigured()
        => !string.IsNullOrEmpty(_oss.AccessKeyId) && !string.IsNullOrEmpty(_oss.BucketName);

    private async Task<string> UploadToLocalAsync(Stream stream, string key, CancellationToken ct)
    {
        var root = Path.IsPathRooted(_upload.LocalRoot)
            ? _upload.LocalRoot
            : Path.Combine(_env.ContentRootPath, _upload.LocalRoot);
        var fullPath = Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        using var fs = File.Create(fullPath);
        stream.Position = 0;
        await stream.CopyToAsync(fs, ct);

        var baseAddr = _upload.LocalBaseUrl.TrimEnd('/');
        return $"{baseAddr}/{key}";
    }

    private Task<string> UploadToOssAsync(Stream stream, string key, CancellationToken ct)
    {
        // TODO: 尚未引入阿里云 OSS SDK，当前实现仅返回拼接的 OSS 风格 URL，
        // 不会真正上传文件流，流数据被丢弃。配置 OSS 时调用方需注意此限制，
        // 或在未配置 OSS 时走 UploadToLocalAsync 分支落盘。
        // 后续需替换为真实 OSS 上传（引入 aliyun-oss-dotnet-sdk）。
        var baseUrl = !string.IsNullOrEmpty(_oss.BaseUrl)
            ? _oss.BaseUrl.TrimEnd('/')
            : $"https://{_oss.BucketName}.{_oss.Endpoint}";
        return Task.FromResult($"{baseUrl}/{key}");
    }

    private static string GetExtension(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return string.Empty;
        var idx = fileName.LastIndexOf('.');
        if (idx < 0 || idx == fileName.Length - 1) return string.Empty;
        return fileName[idx..].ToLowerInvariant();
    }
}
