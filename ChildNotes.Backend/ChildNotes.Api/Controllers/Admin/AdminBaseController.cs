using Microsoft.AspNetCore.Mvc;

namespace ChildNotes.Api.Controllers.Admin;

/// <summary>
/// Admin 端控制器基类：统一 [ApiController] 特性，避免在每个控制器重复声明。
/// </summary>
[ApiController]
public abstract class AdminBaseController : ControllerBase
{
}
