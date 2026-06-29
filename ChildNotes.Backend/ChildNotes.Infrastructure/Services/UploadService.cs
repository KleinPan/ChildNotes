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

    public UploadService(IOptions<OssOptions> oss, IOptions<UploadOptions> upload, IWebHostEnvironment env)
    {
        _oss = oss.Value;
        _upload = upload.Value;
        _env = env;
    }

    public async Task<UploadResponse> UploadAsync(Stream stream, string fileName, CancellationToken ct = default)
    {
        if (stream.Length > _upload.MaxFileSizeBytes)
            throw new BusinessException($"文件大小超过限制（最大 {_upload.MaxFileSizeBytes / 1024 / 1024}MB）");

        var datePath = DateTime.UtcNow.ToString("yyyy/MM/dd");
        var ext = GetExtension(fileName);
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
        // 阶段 2 先不引入阿里云 OSS SDK，阶段 3 补全
        // 目前若配置了 OSS，降级到本地存储并拼接 OSS 风格 URL（便于测试）
        // 阶段 3 替换为真实 OSS 上传
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
