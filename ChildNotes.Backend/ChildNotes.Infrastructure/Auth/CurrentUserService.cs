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
}
