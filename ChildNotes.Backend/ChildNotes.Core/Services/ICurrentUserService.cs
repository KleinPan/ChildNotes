namespace ChildNotes.Core.Services;

public interface ICurrentUserService
{
    string? UserId { get; }
    bool IsAuthenticated { get; }
    string RequireUserId();

    /// <summary>
    /// 从请求头 X-Baby-Id 或查询参数 babyId 解析宝宝 ID，未提供返回 null。
    /// 逻辑与 AppBaseController.ResolveBabyIdFromRequest 一致（服务层复用，避免各自解析）。
    /// </summary>
    string? GetBabyIdOrDefault();
}
