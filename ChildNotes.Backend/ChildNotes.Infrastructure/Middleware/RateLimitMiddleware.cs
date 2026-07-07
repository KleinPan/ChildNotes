using System.Collections.Concurrent;
using ChildNotes.Core.Config;
using ChildNotes.Core.Entities;
using ChildNotes.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChildNotes.Infrastructure.Middleware;

/// <summary>
/// 限流中间件：内存滑动窗口，按 IP + METHOD + 路由模板 维度。
/// 超过 MaxRequestsPerSecond 返回 429，超过 BlacklistRequestsPerSecond 加入内存黑名单返回 403。
/// 注意：黑名单仅存在于当前进程内存（_blacklist 字段），进程重启或多实例部署时不共享、不持久化，
/// 语义上并非真正"永久"，响应文案中的"永久限制"指当前进程生命周期内生效。
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

    public async Task InvokeAsync(HttpContext ctx, ChildNotesDbContext db, IServiceProvider sp)
    {
        if (!_opt.Enabled
            || ctx.Request.Method == "OPTIONS"
            || !ctx.Request.Path.StartsWithSegments("/api"))
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

        var endpoint = $"{ctx.Request.Method} {ctx.Request.Path}";
        var key = $"{ip}|{endpoint}";
        var nowMs = Environment.TickCount64;
        var windowStart = nowMs - 1000;

        var queue = _counters.GetOrAdd(key, _ => new ConcurrentQueue<long>());
        // 清理 1 秒外的旧记录
        while (queue.TryPeek(out var t) && t < windowStart)
        {
            queue.TryDequeue(out _);
        }
        queue.Enqueue(nowMs);
        var count = queue.Count;

        // 定期清理 120 秒未访问的 key
        TryCleanup(nowMs);

        var blacklistThreshold = Math.Max(_opt.BlacklistRequestsPerSecond, _opt.MaxRequestsPerSecond + 1);
        if (count > blacklistThreshold)
        {
            await BlacklistIpAsync(db, ip, ctx.Request.Method, ctx.Request.Path, endpoint, count, nowMs);
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

    private string ResolveClientIp(HttpContext ctx)
    {
        if (_opt.TrustProxyHeaders)
        {
            var xff = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(xff))
            {
                var first = xff.Split(',')[0].Trim();
                if (!string.IsNullOrWhiteSpace(first)) return first;
            }
            var xreal = ctx.Request.Headers["X-Real-IP"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(xreal)) return xreal.Trim();
            var fwd = ctx.Request.Headers["Forwarded"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(fwd))
            {
                var forIdx = fwd.IndexOf("for=", StringComparison.OrdinalIgnoreCase);
                if (forIdx >= 0)
                {
                    var rest = fwd[(forIdx + 4)..];
                    var end = rest.IndexOfAny(new[] { ';', ',' });
                    var val = end >= 0 ? rest[..end] : rest;
                    val = val.Trim().Trim('"').TrimStart('[').TrimEnd(']');
                    if (!string.IsNullOrWhiteSpace(val)) return val;
                }
            }
        }
        return ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
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

    private static async Task BlacklistIpAsync(
        ChildNotesDbContext db, string ip, string method, string path,
        string endpoint, int count, long nowMs)
    {
        var now = DateTime.UtcNow;
        var windowStartedAt = now.AddMilliseconds(-1000);
        // 幂等：已存在则跳过
        if (await db.IpBlacklist.AnyAsync(b => b.IpAddress == ip)) return;

        db.IpBlacklist.Add(new IpBlacklist
        {
            IpAddress = ip,
            TriggerMethod = method,
            TriggerPath = path,
            TriggerEndpoint = endpoint,
            RequestCount = count,
            WindowStartedAt = windowStartedAt,
            Reason = $"同一IP同一接口1秒内请求超过{count}次",
            CreatedAt = now,
            UpdatedAt = now,
        });
        try { await db.SaveChangesAsync(); }
        catch (Exception) { /* 并发幂等 */ }
    }

    private static async Task WriteResponse(HttpContext ctx, int status, string msg)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        await ctx.Response.WriteAsJsonAsync(new
        {
            state = "000520",
            msg = msg,
            data = (object?)null,
        });
    }
}
