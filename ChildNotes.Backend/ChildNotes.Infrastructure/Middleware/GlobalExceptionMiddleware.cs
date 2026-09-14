using System.Text.Json;
using System.Text.Json.Serialization;
using ChildNotes.Core.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChildNotes.Infrastructure.Middleware;

/// <summary>
/// 全局异常中间件：未捕获异常统一包装为 ApiResponse，避免泄漏堆栈。
/// 从 Program.cs 内联 lambda 抽取为独立类，可单测；写响应前检查 HasStarted，
/// 避免响应已部分写出后二次抛异常掩盖原始异常。
/// </summary>
public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly IHostEnvironment _env;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger,
        IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "未处理异常: {Path}", context.Request.Path);

            // 响应已开始写出（如序列化中途异常）：无法再改写状态码/响应体，只能记录后放弃
            if (context.Response.HasStarted) return;

            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json; charset=utf-8";
            // 开发/测试环境暴露异常详情便于排查；生产环境仅返回通用提示
            var apiResp = (_env.IsDevelopment() || _env.IsEnvironment("Testing"))
                ? ApiResponse.Fail($"服务器内部错误：{ex.Message}")
                : ApiResponse.Fail("服务器内部错误，请稍后重试");
            await context.Response.WriteAsync(JsonSerializer.Serialize(apiResp, JsonOpts));
        }
    }
}
