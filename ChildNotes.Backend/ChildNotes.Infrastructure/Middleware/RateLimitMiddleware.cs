using System.Collections.Concurrent;
using ChildNotes.Core.Common;
using ChildNotes.Core.Config;
using ChildNotes.Core.Constants;
using ChildNotes.Core.Entities;
using ChildNotes.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ChildNotes.Infrastructure.Middleware;

/// <summary>
/// 限流中间件：内存滑动窗口，按 IP + METHOD + 路由模板 维度。
/// 超过 MaxRequestsPerSecond 返回 429，超过 BlacklistRequestsPerSecond 加入内存黑名单返回 403。
/// 覆盖 /api/** 与 /admin/api/**（Admin 体系含登录接口，同样需要防爆破）。
/// 注意：黑名单仅存在于当前进程内存（_blacklist 字段），进程重启或多实例部署时不共享、不持久化，
/// 语义上并非真正"永久"，响应文案中的"永久限制"指当前进程生命周期内生效。
///
/// 客户端 IP 解析：直接使用 ctx.Connection.RemoteIpAddress——可信代理场景由 ForwardedHeadersMiddleware
/// （Program.cs 注册，KnownProxies 仅含本机回环）在管道更早处把 X-Forwarded-For 处理后回填到
/// RemoteIpAddress。此处不再自行解析 XFF/X-Real-IP/Forwarded 头：那些头的第一段是客户端可伪造的，
/// 自行解析会让攻击者每个请求换一个伪造 IP 绕过全部限流。
/// </summary>
public class RateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RateLimitOptions _opt;

    // key = ip|endpoint, value = 滑动窗口时间戳队列
    private readonly ConcurrentDictionary<string, ConcurrentQueue<long>> _counters = new();
    private readonly ConcurrentDictionary<string, byte> _blacklist = new();
    private long _lastCleanup = Environment.TickCount64;
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromSeconds(60);

    public RateLimitMiddleware(RequestDelegate next, IOptions<RateLimitOptions> opt)
    {
        _next = next;
        _opt = opt.Value;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (!_opt.Enabled
            || ctx.Request.Method == "OPTIONS"
            || (!ctx.Request.Path.StartsWithSegments("/api")
                && !ctx.Request.Path.StartsWithSegments(AdminConstants.RoutePrefix)))
        {
            await _next(ctx);
            return;
        }

        var ip = ResolveClientIp(ctx);

        // 黑名单检查
        if (_blacklist.ContainsKey(ip))
        {
            await WriteResponse(ctx, 403, "当前IP已被限制访问");
            return;
        }

        // 专用限流：/api/auth/send-code 是 [AllowAnonymous] 接口，攻击者可枚举邮箱骚扰用户/耗尽 SMTP 配额
        // 对该路径增加"同 IP 每小时最多 10 次"的限制（独立于全局 5 req/s 限制）
        if (ctx.Request.Path.Equals("/api/auth/send-code", StringComparison.OrdinalIgnoreCase)
            && ctx.Request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
        {
            var sendCodeKey = $"sendcode|{ip}";
            var nowMs = Environment.TickCount64;
            var windowMs = 3600_000; // 1 小时
            var maxPerHour = 10;

            var queue = _counters.GetOrAdd(sendCodeKey, _ => new ConcurrentQueue<long>());
            var cutoff = nowMs - windowMs;
            while (queue.TryPeek(out var t) && t < cutoff) queue.TryDequeue(out _);
            queue.Enqueue(nowMs);

            if (queue.Count > maxPerHour)
            {
                ctx.Response.Headers["Retry-After"] = "3600";
                await WriteResponse(ctx, 429, $"该IP发送验证码请求过于频繁，每小时限制{maxPerHour}次");
                return;
            }
        }

        // 限流 key 用路由模板（如 /api/records/{id}）而非原始路径：
        // 带路径参数的接口若按原始路径计数，攻击者枚举不同 id 时每个桶各自享有配额，端点级限流失效。
        // WebApplication 自动在管道最前 UseRouting，此处 GetEndpoint() 已可用；未匹配端点时回退原始路径。
        var routeTemplate = (ctx.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? ctx.Request.Path.Value;
        var endpoint = $"{ctx.Request.Method} {routeTemplate}";
        var key = $"{ip}|{endpoint}";
        var nowMs2 = Environment.TickCount64;
        var windowStart = nowMs2 - 1000;

        var queue2 = _counters.GetOrAdd(key, _ => new ConcurrentQueue<long>());
        // 清理 1 秒外的旧记录
        while (queue2.TryPeek(out var t) && t < windowStart)
        {
            queue2.TryDequeue(out _);
        }
        queue2.Enqueue(nowMs2);
        var count = queue2.Count;

        // 定期清理 120 秒未访问的 key
        TryCleanup(nowMs2);

        var blacklistThreshold = Math.Max(_opt.BlacklistRequestsPerSecond, _opt.MaxRequestsPerSecond + 1);
        if (count > blacklistThreshold)
        {
            await BlacklistIpAsync(ctx, ip, endpoint, count, nowMs2);
            _blacklist.TryAdd(ip, 0);
            await WriteResponse(ctx, 403, "请求过于频繁，当前IP已被永久限制访问");
            return;
        }

        if (count > _opt.MaxRequestsPerSecond)
        {
            ctx.Response.Headers["Retry-After"] = "1";
            await WriteResponse(ctx, 429, "请求过于频繁，请稍后再试");
            return;
        }

        await _next(ctx);
    }

    private static string ResolveClientIp(HttpContext ctx)
        => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private void TryCleanup(long nowMs)
    {
        var last = Interlocked.Read(ref _lastCleanup);
        if (nowMs - last < (long)_cleanupInterval.TotalMilliseconds) return;
        if (Interlocked.CompareExchange(ref _lastCleanup, nowMs, last) != last) return;

        var cutoff = nowMs - 120_000;
        foreach (var kv in _counters)
        {
            var q = kv.Value;
            while (q.TryPeek(out var t) && t < cutoff) q.TryDequeue(out _);
            if (q.IsEmpty) _counters.TryRemove(kv.Key, out _);
        }
    }

    /// <summary>触发黑名单时才解析 Scoped DbContext，避免每个请求都创建作用域服务。</summary>
    private static async Task BlacklistIpAsync(HttpContext ctx, string ip, string endpoint, int count, long nowMs)
    {
        var db = ctx.RequestServices.GetRequiredService<ChildNotesDbContext>();
        var now = DateTime.UtcNow;
        var windowStartedAt = now.AddMilliseconds(-1000);
        // 幂等：已存在则跳过
        if (await db.IpBlacklist.AnyAsync(b => b.IpAddress == ip, ctx.RequestAborted)) return;

        db.IpBlacklist.Add(new IpBlacklist
        {
            IpAddress = ip,
            TriggerMethod = ctx.Request.Method,
            TriggerPath = ctx.Request.Path.Value ?? string.Empty,
            TriggerEndpoint = endpoint,
            RequestCount = count,
            WindowStartedAt = windowStartedAt,
            Reason = $"同一IP同一接口1秒内请求超过{count}次",
            CreatedAt = now,
            UpdatedAt = now,
        });
        try { await db.SaveChangesAsync(ctx.RequestAborted); }
        catch (Exception) { /* 并发幂等 */ }
    }

    /// <summary>复用 ApiResponse.Fail 统一错误响应格式（与 BusinessException / AdminAuthMiddleware 一致）。</summary>
    private static async Task WriteResponse(HttpContext ctx, int status, string msg)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        await ctx.Response.WriteAsJsonAsync(ApiResponse.Fail(msg));
    }
}
