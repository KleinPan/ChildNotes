using ChildNotes.Core.Dtos;

namespace ChildNotes.Core.Services;

public interface ISignInService
{
    Task<SignInRuleDto> GetSignInRuleAsync(CancellationToken ct = default);
    Task<SignInSummaryDto> GetSignInSummaryAsync(CancellationToken ct = default);
    /// <summary>
    /// 执行签到并发放积分。不返回 Dashboard——由调用方（Controller）随后调用
    /// <see cref="IPointsService.GetDashboardAsync"/> 以保持 HTTP 响应契约。
    /// </summary>
    Task SignInAsync(CancellationToken ct = default);
}
