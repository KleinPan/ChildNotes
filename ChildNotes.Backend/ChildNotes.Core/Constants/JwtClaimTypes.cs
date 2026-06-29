namespace ChildNotes.Core.Constants;

/// <summary>
/// JWT Claim 类型常量，消除 JwtTokenService 等处的字符串硬编码。
/// </summary>
public static class JwtClaimTypes
{
    /// <summary>用户 ID 自定义 claim（与 Program.cs 的 NameClaimType 对齐）。</summary>
    public const string UserId = "uid";
}
