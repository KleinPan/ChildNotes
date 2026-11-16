using ChildNotes.Core.Dtos;

namespace ChildNotes.Core.Services;

/// <summary>
/// HA（Home Assistant）集成数据服务：以 API Key 映射的用户身份查询喂养记录，
/// 不依赖 ICurrentUserService（HA 请求无 JWT 上下文）。
/// </summary>
public interface IHaFeedService
{
    /// <summary>
    /// 查询指定用户默认宝宝某日（默认北京今天）的全部喂养记录 + 汇总。
    /// </summary>
    Task<HaFeedingDay> GetFeedingDayAsync(string email, DateTime? date, CancellationToken ct = default);
}
