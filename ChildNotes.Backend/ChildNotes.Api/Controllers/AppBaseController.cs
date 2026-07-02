using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChildNotes.Api.Controllers;

/// <summary>
/// App 端控制器基类：统一 [ApiController] + [Authorize] 特性。
/// 所有继承此基类的控制器默认需要 JWT 鉴权，无需登录的端点用 [AllowAnonymous] 显式放开。
/// </summary>
[ApiController]
[Authorize]
public abstract class AppBaseController : ControllerBase
{
    /// <summary>
    /// 从请求头 X-Baby-Id 或查询参数 babyId 解析宝宝 ID，未提供返回 null。
    /// </summary>
    protected string? ResolveBabyIdFromRequest()
    {
        if (Request.Headers.TryGetValue("X-Baby-Id", out var h) && !string.IsNullOrEmpty(h)) return h.ToString();
        var q = Request.Query["babyId"].ToString();
        return string.IsNullOrEmpty(q) ? null : q;
    }
}
