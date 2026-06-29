using Microsoft.AspNetCore.Mvc;

namespace ChildNotes.Api.Controllers;

/// <summary>
/// App 端控制器基类：统一 [ApiController] 特性，避免在每个控制器重复声明。
/// </summary>
[ApiController]
public abstract class AppBaseController : ControllerBase
{
    /// <summary>
    /// 从请求头 X-Baby-Id 或查询参数 babyId 解析宝宝 ID，未提供或格式错误返回 null。
    /// </summary>
    protected long? ResolveBabyIdFromRequest()
    {
        if (Request.Headers.TryGetValue("X-Baby-Id", out var h) && long.TryParse(h, out var id)) return id;
        if (long.TryParse(Request.Query["babyId"], out var q)) return q;
        return null;
    }
}
