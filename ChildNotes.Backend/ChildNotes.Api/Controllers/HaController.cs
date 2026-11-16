using System.Security.Cryptography;
using System.Text;
using ChildNotes.Core.Common;
using ChildNotes.Core.Config;
using ChildNotes.Core.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ChildNotes.Api.Controllers;

/// <summary>
/// HA（Home Assistant）集成专用接口：不走 JWT，用 X-Api-Key 头鉴权。
/// Key 与用户邮箱的映射配置在 Ha:ApiKeys（生产配置 appsettings.Production.json）。
/// 刻意不继承 AppBaseController（App 端统一 [Authorize] JWT），避免影响现有鉴权语义。
/// </summary>
[ApiController]
[Route("api/ha")]
public class HaController : ControllerBase
{
    private readonly HaOptions _options;
    private readonly IHaFeedService _haFeed;

    public HaController(IOptions<HaOptions> options, IHaFeedService haFeed)
    {
        _options = options.Value;
        _haFeed = haFeed;
    }

    /// <summary>
    /// 查询某日（默认北京今天）全部喂养记录 + 汇总，供 HA 定时拉取（默认 10 分钟）。
    /// </summary>
    [HttpGet("feeding")]
    public async Task<object> GetFeeding([FromQuery] DateTime? date, CancellationToken ct)
    {
        var email = ResolveEmail();
        if (email == null)
            return Unauthorized(ApiResponse.Fail("无效的 API 密钥", "INVALID_API_KEY"));

        try
        {
            return await _haFeed.GetFeedingDayAsync(email, date, ct);
        }
        catch (OperationCanceledException)
        {
            throw; // 客户端断开/停机，交给框架处理，勿吞
        }
        catch (ChildNotes.Core.Exceptions.BusinessException)
        {
            throw; // 业务异常由 ApiResponseWrapperFilter 统一转信封
        }
        catch (Exception ex)
        {
            // 兜底转统一 JSON 信封，避免 HA 收到非 JSON 的原始 500 页面
            return StatusCode(500, ApiResponse.Fail($"服务器内部错误: {ex.Message}", "INTERNAL_ERROR"));
        }
    }

    /// <summary>X-Api-Key → 用户邮箱。逐 Key 常量时间比较，防时序侧信道；未配置/未命中返回 null。</summary>
    private string? ResolveEmail()
    {
        if (_options.ApiKeys.Count == 0) return null;
        if (!Request.Headers.TryGetValue("X-Api-Key", out var key) || string.IsNullOrEmpty(key)) return null;

        foreach (var (apiKey, email) in _options.ApiKeys)
        {
            if (FixedTimeEquals(apiKey, key.ToString())) return email;
        }
        return null;
    }

    private static bool FixedTimeEquals(string a, string b)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
