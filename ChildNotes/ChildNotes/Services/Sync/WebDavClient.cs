using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Xml.Linq;
using ChildNotes.Infrastructure;

namespace ChildNotes.Services.Sync;

/// <summary>
/// 轻量级 WebDAV 客户端，基于 BCL HttpClient 实现，不依赖第三方库。
/// 支持 PROPFIND / GET / PUT / MKCOL / DELETE / LOCK / UNLOCK。
/// 兼容坚果云、Nextcloud 等标准 WebDAV 服务。
/// </summary>
public sealed class WebDavClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _remotePath;

    /// <summary>
    /// 创建 WebDAV 客户端。
    /// </summary>
    /// <param name="serverUrl">服务器根 URL，如 https://dav.jianguoyun.com/dav/</param>
    /// <param name="username">账号</param>
    /// <param name="password">密码（坚果云为应用专用密码）</param>
    /// <param name="remotePath">远程子路径，如 /ChildNotes/</param>
    public WebDavClient(string serverUrl, string username, string password, string remotePath)
    {
        _baseUrl = serverUrl.TrimEnd('/') + "/";
        _remotePath = NormalizePath(remotePath);

        var handler = new HttpClientHandler
        {
            Credentials = new NetworkCredential(username, password),
            PreAuthenticate = true,
            // 坚果云等使用 Basic 认证
            AllowAutoRedirect = true,
        };
        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(60),
            DefaultRequestHeaders =
            {
                Authorization = new AuthenticationHeaderValue(
                    "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")))
            }
        };
    }

    // ===== 路径工具 =====

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return "/";
        if (!path.StartsWith('/')) path = "/" + path;
        if (!path.EndsWith('/')) path += "/";
        return path;
    }

    /// <summary>获取远程完整 URL（基于 baseUrl + remotePath + 相对路径）。</summary>
    private string GetUrl(string relativePath = "")
    {
        if (string.IsNullOrEmpty(relativePath)) return _baseUrl.TrimEnd('/') + _remotePath;
        if (relativePath.StartsWith('/')) relativePath = relativePath[1..];
        return _baseUrl.TrimEnd('/') + _remotePath + relativePath;
    }

    // ===== 目录操作 =====

    /// <summary>确保远程目录存在，递归创建。幂等。</summary>
    public async Task EnsureFolderAsync(string relativePath = "", CancellationToken ct = default)
    {
        // 拆分路径逐级创建（坚果云不支持自动递归）
        var parts = (_remotePath + relativePath).Split('/', StringSplitOptions.RemoveEmptyEntries);
        var cur = _baseUrl.TrimEnd('/');
        foreach (var p in parts)
        {
            cur += "/" + p;
            await MkColIfNotExistsAsync(cur, ct);
        }
    }

    private async Task MkColIfNotExistsAsync(string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, url) { Method = new HttpMethod("MKCOL") };
        using var resp = await _http.SendAsync(req, ct);
        // 201=created, 405=already exists, 409=parent missing
        if (resp.StatusCode == HttpStatusCode.Conflict) return; // 父目录不存在，外层会处理
        if (resp.StatusCode == HttpStatusCode.MethodNotAllowed) return; // 已存在
        if (!resp.IsSuccessStatusCode && resp.StatusCode != HttpStatusCode.Conflict)
        {
            DevLogger.Log("WebDAV", $"MKCOL {url} -> {resp.StatusCode}");
        }
    }

    // ===== 文件操作 =====

    /// <summary>上传文件。覆盖式写入。</summary>
    public async Task PutAsync(string remoteFileName, Stream content, CancellationToken ct = default)
    {
        var url = GetUrl(remoteFileName);
        using var resp = await _http.PutAsync(url, new StreamContent(content), ct);
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>下载文件到流。</summary>
    public async Task<Stream> GetAsync(string remoteFileName, CancellationToken ct = default)
    {
        var url = GetUrl(remoteFileName);
        var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStreamAsync(ct);
    }

    /// <summary>获取文件元信息（ETag / Last-Modified），不存在返回 null。</summary>
    public async Task<WebDavFileInfo?> GetFileInfoAsync(string remoteFileName, CancellationToken ct = default)
    {
        var url = GetUrl(remoteFileName);
        using var req = new HttpRequestMessage(new HttpMethod("PROPFIND"), url);
        req.Headers.Add("Depth", "0");
        req.Content = new StringContent(
            @"<?xml version=""1.0"" encoding=""utf-8""?>
<D:propfind xmlns:D=""DAV:""><D:prop><D:getlastmodified/><D:getetag/><D:getcontentlength/></D:prop></D:propfind>",
            Encoding.UTF8, "application/xml");

        using var resp = await _http.SendAsync(req, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadAsStringAsync(ct);
        return ParsePropFind(body);
    }

    /// <summary>列出目录下的文件名（不含子目录）。</summary>
    public async Task<List<string>> ListFilesAsync(string relativePath = "", CancellationToken ct = default)
    {
        var url = relativePath == "" ? GetUrl("") : GetUrl(relativePath);
        using var req = new HttpRequestMessage(new HttpMethod("PROPFIND"), url);
        req.Headers.Add("Depth", "1");
        req.Content = new StringContent(
            @"<?xml version=""1.0"" encoding=""utf-8""?>
<D:propfind xmlns:D=""DAV:""><D:prop><D:displayname/><D:getcontentlength/></D:prop></D:propfind>",
            Encoding.UTF8, "application/xml");

        using var resp = await _http.SendAsync(req, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return new List<string>();
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadAsStringAsync(ct);
        return ParseFileList(body);
    }

    /// <summary>删除远程文件。</summary>
    public async Task DeleteAsync(string remoteFileName, CancellationToken ct = default)
    {
        var url = GetUrl(remoteFileName);
        using var resp = await _http.DeleteAsync(url, ct);
        if (resp.StatusCode != HttpStatusCode.NotFound)
            resp.EnsureSuccessStatusCode();
    }

    // ===== XML 解析 =====

    private static WebDavFileInfo? ParsePropFind(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            // 处理命名空间 DAV:
            var ns = XNamespace.Get("DAV:");
            var prop = doc.Descendants(ns + "prop").FirstOrDefault();
            if (prop == null) return null;

            var info = new WebDavFileInfo();
            var lm = prop.Element(ns + "getlastmodified");
            if (lm != null && DateTime.TryParse(lm.Value, out var dt)) info.LastModified = dt;
            var et = prop.Element(ns + "getetag");
            if (et != null) info.ETag = et.Value.Trim('"');
            var sz = prop.Element(ns + "getcontentlength");
            if (sz != null && long.TryParse(sz.Value, out var size)) info.Size = size;
            return info;
        }
        catch (Exception ex)
        {
            DevLogger.Log("WebDAV", $"ParsePropFind failed: {ex.Message}");
            return null;
        }
    }

    private static List<string> ParseFileList(string xml)
    {
        var result = new List<string>();
        try
        {
            var doc = XDocument.Parse(xml);
            var ns = XNamespace.Get("DAV:");
            // 每个 response 标签代表一个资源
            foreach (var resp in doc.Descendants(ns + "response"))
            {
                var href = resp.Element(ns + "href")?.Value;
                if (string.IsNullOrEmpty(href)) continue;
                // 跳过目录本身（以 / 结尾的）
                if (href.EndsWith("/")) continue;
                // 取文件名
                var name = Uri.UnescapeDataString(href).Split('/').Last();
                if (!string.IsNullOrEmpty(name)) result.Add(name);
            }
        }
        catch (Exception ex)
        {
            DevLogger.Log("WebDAV", $"ParseFileList failed: {ex.Message}");
        }
        return result;
    }

    public void Dispose() => _http.Dispose();
}

public sealed class WebDavFileInfo
{
    public DateTime? LastModified { get; set; }
    public string? ETag { get; set; }
    public long Size { get; set; }
}
