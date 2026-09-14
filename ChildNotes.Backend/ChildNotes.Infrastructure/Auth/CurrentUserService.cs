using ChildNotes.Core.Exceptions;
using ChildNotes.Core.Services;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace ChildNotes.Infrastructure.Auth;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _accessor;
    public CurrentUserService(IHttpContextAccessor accessor) => _accessor = accessor;

    public string? UserId
    {
        get
        {
            var ctx = _accessor.HttpContext;
            if (ctx is null) return null;
            var uidStr = ctx.User.FindFirst("uid")?.Value
                ?? ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return string.IsNullOrEmpty(uidStr) ? null : uidStr;
        }
    }

    public bool IsAuthenticated => !string.IsNullOrEmpty(UserId);

    public string RequireUserId()
    {
        var uid = UserId;
        if (string.IsNullOrEmpty(uid)) throw new UnauthorizedException();
        return uid;
    }

    public string? GetBabyIdOrDefault()
    {
        // 与 AppBaseController.ResolveBabyIdFromRequest 逻辑一致：优先 X-Baby-Id 请求头，其次 babyId 查询参数
        var ctx = _accessor.HttpContext;
        if (ctx is null) return null;
        if (ctx.Request.Headers.TryGetValue("X-Baby-Id", out var h) && !string.IsNullOrEmpty(h)) return h.ToString();
        var q = ctx.Request.Query["babyId"].ToString();
        return string.IsNullOrEmpty(q) ? null : q;
    }
}
