using ChildNotes.Core.Config;
using ChildNotes.Core.Dtos;
using ChildNotes.Core.Services;
using ChildNotes.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ChildNotes.Api.Controllers;

/// <summary>
/// 会员 API：套餐查询、会员状态、订单创建、订单状态查询、支付宝回调。
/// 回调接口允许匿名访问（支付宝服务器调用）。
/// </summary>
[Route("api/membership")]
public class MembershipController : AppBaseController
{
    private readonly IMembershipService _membership;
    private readonly MembershipOptions _opt;

    public MembershipController(IMembershipService membership, IOptions<MembershipOptions> opt)
    {
        _membership = membership;
        _opt = opt.Value;
    }

    /// <summary>获取所有可用套餐。</summary>
    [HttpGet("plans")]
    public async Task<List<MembershipPlanDto>> Plans(CancellationToken ct)
        => await _membership.GetPlansAsync(ct);

    /// <summary>获取当前用户的会员状态（含 AI 次数信息）。</summary>
    [HttpGet("status")]
    public async Task<MembershipStatusDto> Status(CancellationToken ct)
        => await _membership.GetStatusAsync(ct);

    /// <summary>创建支付订单，返回支付参数（支付宝 orderInfo）。</summary>
    [HttpPost("orders")]
    public async Task<CreateOrderResponse> CreateOrder([FromBody] CreateOrderRequest req, CancellationToken ct)
        => await _membership.CreateOrderAsync(req ?? new(), ct);

    /// <summary>查询订单状态（支付完成后轮询用）。</summary>
    [HttpGet("orders/{orderNo}")]
    public async Task<OrderStatusResponse> GetOrderStatus(string orderNo, CancellationToken ct)
        => await _membership.GetOrderStatusAsync(orderNo, ct);

    /// <summary>
    /// 支付宝异步回调通知。由支付宝服务器调用，无需鉴权。
    /// 返回 "success" 表示处理成功，其他值支付宝会重试通知。
    /// </summary>
    [HttpPost("alipay/notify")]
    [AllowAnonymous]
    public async Task<ContentResult> AlipayNotify(CancellationToken ct)
    {
        var form = new Dictionary<string, string>();
        foreach (var key in Request.Form.Keys)
        {
            var value = Request.Form[key].ToString();
            if (!string.IsNullOrEmpty(value))
                form[key] = value;
        }
        var result = await _membership.HandleAlipayNotifyAsync(form, ct);
        return Content(result, "text/plain");
    }

    /// <summary>
    /// 为当前用户激活永不过期会员（开发版 APK 调用）。
    /// 仅在 MembershipOptions.EnableDevAutoActivate=true 时可用，否则返回 404。
    /// </summary>
    [HttpPost("dev/activate")]
    public async Task<IActionResult> DevActivate(CancellationToken ct)
    {
        if (!_opt.EnableDevAutoActivate)
            return NotFound();
        await _membership.DevActivatePermanentAsync(ct);
        return Ok(new { state = 0, msg = "ok" });
    }
}
