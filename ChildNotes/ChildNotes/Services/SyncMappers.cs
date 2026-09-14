using ChildNotes.Data;
using ChildNotes.Models;
using ChildNotes.Shared.Sync;

namespace ChildNotes.Services;

/// <summary>
/// 同步实体映射（本地实体 ↔ 共享同步 DTO，ChildNotes.Shared.Sync）。
/// 从 ApiSyncService 拆出的纯函数，原样搬移；ApiSyncService 继续调用。
/// </summary>
internal static class SyncMappers
{
    /// <summary>
    /// Pull 映射（家庭业务表）：本地 UserId 一律写 LocalDataSpaceId（设计文档 6.2，幂等），
    /// 禁止透传云端 UserId（家庭数据本地可见性与登录态无关）。
    /// </summary>
    internal static Baby MapToBaby(SyncBabyItem i, string localDataSpaceId) => new()
    {
        Id = i.Id, UserId = localDataSpaceId, Name = i.Name, Avatar = i.Avatar ?? "",
        Gender = i.Gender ?? "", BirthDate = i.BirthDate,
        // 服务器时间约定为 UTC，转 Local 与本地库读取行为一致；BirthDate 是纯日期原样保留
        CreatedAt = ToLocal(i.CreatedAt), UpdatedAt = ToLocal(i.UpdatedAt),
    };

    /// <summary>Pull 映射（家庭业务表），本地 UserId 写 LocalDataSpaceId（规则同 MapToBaby）。</summary>
    internal static ChildRecord MapToRecord(SyncRecordItem i, string localDataSpaceId) => new()
    {
        Id = i.Id, UserId = localDataSpaceId, BabyId = i.BabyId,
        RecordType = i.RecordType, RecordSubType = i.RecordSubType,
        // 服务器传来的时间约定为 UTC（后端 SyncService 用 SpecifyKind(..., Utc) 标记）。
        // 但 DTO 用 DateTime 传输、JSON 反序列化后 Kind=Unspecified。这里显式转 Local，
        // 与 RecordRepository.Map 读本地库的行为一致，使应用层统一感知本地时间。
        // 写库时 AddUtc 会再次转回 UTC（幂等）。
        RecordDate = i.RecordDate,
        RecordTime = ToLocal(i.RecordTime),
        AmountMl = i.AmountMl, DurationSec = i.DurationSec,
        LeftDurationSec = i.LeftDurationSec, RightDurationSec = i.RightDurationSec,
        AbnormalFlag = i.AbnormalFlag, TemperatureValue = i.TemperatureValue,
        HeightCm = i.HeightCm, WeightKg = i.WeightKg,
        PayloadJson = i.PayloadJson ?? "{}", Deleted = i.Deleted,
        CreatedAt = ToLocal(i.CreatedAt), UpdatedAt = ToLocal(i.UpdatedAt),
    };

    /// <summary>
    /// 把同步 DTO 反序列化后的 DateTime 视为 UTC 并转 Local。
    /// 反序列化时 Kind 通常为 Unspecified（JSON 无时区信息时）或 Utc（带 Z 时），
    /// 二者都先 SpecifyKind(Utc) 再 ToLocal，保证应用层始终拿到本地时间。
    /// </summary>
    internal static DateTime ToLocal(DateTime dt)
        => (dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc)).ToLocalTime();

    /// <summary>把应用层的本地时间转回 UTC，用于上送服务器。
    /// 同时截断到微秒精度：存量数据（本次修复前写入）仍带 100ns 余数，
    /// 若原样上送，服务端 PostgreSQL 截断后与存储值相等，LWW 严格大于判断失败被跳过。</summary>
    internal static DateTime ToUtc(DateTime dt)
        => (dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime()).TruncateToMicroseconds();

    /// <summary>
    /// Push 映射（家庭业务表）：UserId 注入当前 CloudUserId（创建者归因），FamilyId 注入当前绑定家庭。
    /// 禁止读本地实体 user_id（语义已降级为 LocalDataSpaceId）；服务端以 JWT 鉴权为准，payload 仅路由/日志。
    /// </summary>
    internal static SyncBabyItem MapToBabyItem(Baby b, string cloudUserId, string familyId) => new()
    {
        Id = b.Id, UserId = cloudUserId, FamilyId = familyId, Name = b.Name, Avatar = b.Avatar ?? "",
        Gender = b.Gender ?? "", BirthDate = b.BirthDate,
        // 应用层时间已是 Local，上送服务器需转 UTC；BirthDate 是纯日期原样上送
        CreatedAt = ToUtc(b.CreatedAt), UpdatedAt = ToUtc(b.UpdatedAt),
    };

    /// <summary>Push 映射（家庭业务表），身份注入规则同 <see cref="MapToBabyItem"/>。</summary>
    internal static SyncRecordItem MapToRecordItem(ChildRecord r, string cloudUserId, string familyId) => new()
    {
        Id = r.Id, UserId = cloudUserId, FamilyId = familyId, BabyId = r.BabyId,
        RecordType = r.RecordType, RecordSubType = r.RecordSubType,
        // 应用层 RecordTime/CreatedAt/UpdatedAt 已是 Local（RecordRepository.Map 转换过）。
        // 服务器期望 UTC，这里显式转回。RecordDate 是纯日期无时区，原样上送。
        RecordDate = r.RecordDate,
        RecordTime = ToUtc(r.RecordTime),
        AmountMl = r.AmountMl, DurationSec = r.DurationSec,
        LeftDurationSec = r.LeftDurationSec, RightDurationSec = r.RightDurationSec,
        AbnormalFlag = r.AbnormalFlag, TemperatureValue = r.TemperatureValue,
        HeightCm = r.HeightCm, WeightKg = r.WeightKg,
        PayloadJson = r.PayloadJson ?? "{}", Deleted = r.Deleted,
        CreatedAt = ToUtc(r.CreatedAt), UpdatedAt = ToUtc(r.UpdatedAt),
    };

    /// <summary>Pull 映射（家庭业务表），本地 UserId 写 LocalDataSpaceId（规则同 MapToBaby）。</summary>
    internal static Milestone MapToMilestone(SyncMilestoneItem i, string localDataSpaceId) => new()
    {
        Id = i.Id, UserId = localDataSpaceId, BabyId = i.BabyId,
        Title = i.Title, Content = i.Content,
        // RecordDate 是纯日期，原样保留；CreatedAt/UpdatedAt 服务器传 UTC，转 Local
        RecordDate = i.RecordDate,
        PhotosJson = string.IsNullOrEmpty(i.PhotosJson) ? "[]" : i.PhotosJson,
        Deleted = i.Deleted,
        CreatedAt = ToLocal(i.CreatedAt), UpdatedAt = ToLocal(i.UpdatedAt),
    };

    /// <summary>Push 映射（家庭业务表）：UserId 注入当前 CloudUserId（创建者透传），FamilyId 注入当前绑定家庭。</summary>
    internal static SyncMilestoneItem MapToMilestoneItem(Milestone m, string cloudUserId, string familyId) => new()
    {
        Id = m.Id, UserId = cloudUserId, FamilyId = familyId, BabyId = m.BabyId,
        Title = m.Title, Content = m.Content,
        RecordDate = m.RecordDate,
        PhotosJson = m.PhotosJson ?? "[]",
        Deleted = m.Deleted,
        // 应用层 Local 时间上送服务器转 UTC
        CreatedAt = ToUtc(m.CreatedAt), UpdatedAt = ToUtc(m.UpdatedAt),
    };

    /// <summary>Pull 映射（个人数据）：签到 UserId 写当前 CloudUserId（设计文档 6.2）。</summary>
    internal static SignInRecord MapToSignIn(SyncSignInItem i, string cloudUserId) => new()
    {
        Id = i.Id, UserId = cloudUserId,
        SignDate = i.SignDate,
        ContinuousDays = i.ContinuousDays,
        Reward = i.Reward,
        CreatedAt = ToLocal(i.CreatedAt),
    };

    /// <summary>
    /// Push 映射（个人数据）：签到 UserId 注入当前 CloudUserId（不随家庭切换）。
    /// 离线期间以 LocalUserId 创建的签到，登录后按此归因到账号（服务端校验 item.UserId == JWT uid）。
    /// </summary>
    internal static SyncSignInItem MapToSignInItem(SignInRecord s, string cloudUserId) => new()
    {
        Id = s.Id, UserId = cloudUserId,
        SignDate = s.SignDate,
        ContinuousDays = s.ContinuousDays,
        Reward = s.Reward,
        CreatedAt = ToUtc(s.CreatedAt),
    };

    /// <summary>Pull 映射（个人数据）：积分余额 UserId 写当前 CloudUserId（服务端为准覆盖本地）。</summary>
    internal static UserPoints MapToUserPoints(SyncUserPointsItem i, string cloudUserId) => new()
    {
        Id = i.Id, UserId = cloudUserId,
        Points = i.Points, TotalEarned = i.TotalEarned, TotalSpent = i.TotalSpent,
        // user_points 本地无独立 CreatedAt 同步，用 UpdatedAt 近似（仅 LWW 判定用）
        CreatedAt = ToLocal(i.UpdatedAt), UpdatedAt = ToLocal(i.UpdatedAt),
    };
}
