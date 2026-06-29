using ChildNotes.Core.Exceptions;
using ChildNotes.Core.Services;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace ChildNotes.Infrastructure.Auth;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _accessor;
    public CurrentUserService(IHttpContextAccessor accessor) => _accessor = accessor;

    public long? UserId
    {
        get
        {
            var ctx = _accessor.HttpContext;
            if (ctx is null) return null;
            var uidStr = ctx.User.FindFirst("uid")?.Value
                ?? ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return long.TryParse(uidStr, out var uid) ? uid : null;
        }
    }

    public bool IsAuthenticated => UserId.HasValue;

    public long RequireUserId()
    {
        if (UserId is long uid) return uid;
        throw new UnauthorizedException();
    }
}
