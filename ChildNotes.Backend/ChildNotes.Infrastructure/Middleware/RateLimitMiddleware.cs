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
///
/// 黑名单持久化（#19）：写库（ip_blacklist 表）+ 内存缓存双写。启动时构造器从表全量加载，
/// 运行期每 5 分钟惰性重载（首个触发检查的请求拉起），进程重启后黑名单不丢、多实例一致。
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
    private readonly IServiceScopeFactory _scopeFactory;

    // key = ip|endpoint, value = 滑动窗口时间戳队列
    private readonly ConcurrentDictionary<string, ConcurrentQueue<long>> _counters = new();
    private readonly ConcurrentDictionary<string, byte> _blacklist = new();
    private long _lastCleanup = Environment.TickCount64;
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromSeconds(60);

    // #19 黑名单重载状态：首次请求时构造器无法异步查库，改为惰性加载；
    // 之后每 5 分钟重载一次（DB 有管理端手动加黑等场景，重载保证最终一致）
    private long _lastBlacklistLoad = 0; // 0 = 尚未加载过
    private static readonly long BlacklistReloadIntervalMs = (long)TimeSpan.FromMinutes(5).TotalMilliseconds;
    private int _blacklistLoading = 0;

    public RateLimitMiddleware(RequestDelegate next, IOptions<RateLimitOptions> opt,
        IServiceScopeFactory scopeFactory)
    {
        _next = next;
        _opt = opt.Value;
        _scopeFactory = scopeFactory;
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

        // #19 黑名单惰性加载/定期重载：首次请求或距上次加载超过 5 分钟时从库刷新。
        // 失败不阻塞请求（降级为仅用内存黑名单）。
        await TryReloadBlacklistAsync();

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

    /// <summary>
    /// #19：从 ip_blacklist 表惰性加载/定期重载内存黑名单。
    /// Interlocked 保证只有一个请求真正执行查库，其余请求直接放行（用当前内存集合）；
    /// 查库失败静默降级（内存黑名单仍有效），不影响正常请求。
    /// </summary>
    private async Task TryReloadBlacklistAsync()
    {
        var nowMs = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastBlacklistLoad);
        if (last != 0 && nowMs - last < BlacklistReloadIntervalMs) return;
        if (Interlocked.CompareExchange(ref _lastBlacklistLoad, nowMs, last) != last) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ChildNotesDbContext>();
            var ips = await db.IpBlacklist.Select(b => b.IpAddress).ToListAsync();
            foreach (var ip in ips) _blacklist.TryAdd(ip, 0);
        }
        catch
        {
            // 查库失败：保留现有内存黑名单，下个重载周期再试
        }
    }

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
