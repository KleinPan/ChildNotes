using ChildNotes.Core.Common;
using ChildNotes.Core.Constants;
using ChildNotes.Core.Exceptions;
using ChildNotes.Core.Services;
using ChildNotes.Infrastructure.Auth;
using Microsoft.AspNetCore.Http;

namespace ChildNotes.Infrastructure.Middleware;

/// <summary>
/// Admin 认证中间件：拦截 /admin/api/**，校验数据库 token。
/// 复用 IAdminAuthService.AuthenticateAsync 与 ApiResponse.Fail，消除重复查询与响应包装代码。
/// </summary>
public class AdminAuthMiddleware
{
    private readonly RequestDelegate _next;

    public AdminAuthMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx, IAdminAuthService adminAuth, ICurrentAdminService currentAdmin)
    {
        if (!ctx.Request.Path.StartsWithSegments(AdminConstants.RoutePrefix))
        {
            await _next(ctx);
            return;
        }

        // 登录接口放行
        if (ctx.Request.Path.StartsWithSegments(AdminConstants.LoginPath))
        {
            await _next(ctx);
            return;
        }

        if (ctx.Request.Method == "OPTIONS")
        {
            await _next(ctx);
            return;
        }

        var token = ResolveToken(ctx);
        if (string.IsNullOrEmpty(token))
        {
            await WriteUnauthorized(ctx, AdminConstants.AdminLoginRequiredMsg);
            return;
        }

        // 复用 AdminAuthService.AuthenticateAsync，避免中间件直接访问 DbContext 重复查询逻辑
        var admin = await adminAuth.AuthenticateAsync(token);
        if (admin is null)
        {
            await WriteUnauthorized(ctx, AdminConstants.AdminLoginRequiredMsg);
            return;
        }

        currentAdmin.SetAdmin(admin);
        ctx.Items[AdminConstants.CurrentAdminItemKey] = admin;
        await _next(ctx);
    }

    private static string? ResolveToken(HttpContext ctx)
    {
        var auth = ctx.Request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrEmpty(auth))
        {
            if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return auth[7..].Trim();
            return auth.Trim();
        }
        var tokenHeader = ctx.Request.Headers["token"].FirstOrDefault();
        if (!string.IsNullOrEmpty(tokenHeader)) return tokenHeader.Trim();
        var tokenQuery = ctx.Request.Query["token"].FirstOrDefault();
        if (!string.IsNullOrEmpty(tokenQuery)) return tokenQuery.Trim();
        return null;
    }

    /// <summary>复用 ApiResponse.Fail 统一响应格式，消除硬编码 state/msg/data 字段。</summary>
    private static async Task WriteUnauthorized(HttpContext ctx, string msg)
    {
        ctx.Response.StatusCode = 401;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        await ctx.Response.WriteAsJsonAsync(ApiResponse.Fail<object?>(msg));
    }
}
