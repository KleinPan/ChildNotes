namespace ChildNotes.Services;

/// <summary>
/// 图片路径解析工具：统一处理服务器相对路径（/uploads/、/api/）到完整 URL 的拼接。
///
/// 后端 UploadService 返回的是相对路径（如 /uploads/2024/01/01/xxx.jpg），
/// 跨设备同步后本地无此文件，必须拼成完整 URL 才能加载。
/// 此逻辑原本散落在 AvatarImage、BabyManagerViewModel.LoadAvatarFromPath、
/// GrowthViewModel 等多处，统一抽取到此处避免"修一处漏一处"。
/// </summary>
public static class ImagePathResolver
{
    /// <summary>
    /// 将路径规范化为可直接加载的形式：
    /// - 服务器相对路径（/uploads/、/api/）→ 拼接 ServerEndpoints.Primary 后返回完整 URL
    /// - http/https URL → 原样返回
    /// - 本地文件路径 → 原样返回
    /// </summary>
    public static string Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;

        // 服务器相对路径：拼接主服务器地址
        if (path.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            return ServerEndpoints.Primary.TrimEnd('/') + path;
        }

        return path;
    }

    /// <summary>判断路径是否为 HTTP/HTTPS URL。</summary>
    public static bool IsHttpUrl(string path)
        => path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
           path.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
