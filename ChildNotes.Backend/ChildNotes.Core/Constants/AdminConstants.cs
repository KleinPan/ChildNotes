namespace ChildNotes.Core.Constants;

/// <summary>
/// Admin 端通用常量：路由前缀、登录路径、错误消息等。
/// 消除 AdminAuthMiddleware 等处的硬编码字符串。
/// </summary>
public static class AdminConstants
{
    public const string RoutePrefix = "/admin/api";
    public const string LoginPath = "/admin/api/auth/login";
    public const string AdminLoginRequiredMsg = "Admin login is required";

    /// <summary>HttpContext.Items key 用于传递当前 Admin。</summary>
    public const string CurrentAdminItemKey = "CURRENT_ADMIN";

    /// <summary>Authorization Header 中 Bearer 前缀。</summary>
    public const string BearerPrefix = "Bearer ";

    /// <summary>Admin token 在 HTTP Header 中的字段名（兼容旧客户端）。</summary>
    public const string TokenHeader = "token";
}
