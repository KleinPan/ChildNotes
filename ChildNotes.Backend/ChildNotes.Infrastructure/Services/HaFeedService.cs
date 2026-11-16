using System.Text.Json;
using ChildNotes.Core.Dtos;
using ChildNotes.Core.Entities;
using ChildNotes.Core.Exceptions;
using ChildNotes.Core.Services;
using ChildNotes.Shared.Constants;
using ChildNotes.Shared.Dtos;
using ChildNotes.Shared.Services;
using ChildNotes.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChildNotes.Infrastructure.Services;

/// <summary>
/// HA 集成数据服务实现。与 RecordService 相同的业务日口径（北京自然日），
/// 但身份来源是 Ha:ApiKeys 配置的邮箱而非 JWT。
/// </summary>
public class HaFeedService : IHaFeedService
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ChildNotesDbContext _db;
    private readonly IBabyAccessService _babyAccess;

    public HaFeedService(ChildNotesDbContext db, IBabyAccessService babyAccess)
    {
        _db = db;
        _babyAccess = babyAccess;
    }

    public async Task<HaFeedingDay> GetFeedingDayAsync(string email, DateTime? date, CancellationToken ct = default)
    {
        var user = await _db.AppUsers.AsNoTracking()
            .SingleOrDefaultAsync(u => u.Email == email, ct)
            ?? throw new BusinessException("API Key 对应的用户不存在", 401, "INVALID_API_KEY");

        // 业务日 = 北京时间自然日（与 RecordService.GetTodayRecordsAsync 口径一致）；
        // RecordDate 按北京墙钟日期存储（Kind=Utc 满足 timestamptz 参数要求）
        var targetDate = date?.Date ?? ChinaTime.Today;
        var baby = await _babyAccess.GetDefaultBabyAsync(user.Id, ct);
        var babyId = baby?.Id;

        var records = await _db.ChildRecords.AsNoTracking()
            .Where(r => r.UserId == user.Id && r.BabyId == babyId
                        && r.RecordType == RecordType.Feed && r.RecordDate == targetDate)
            .OrderBy(r => r.RecordTime).ToListAsync(ct);

        return BuildResponse(targetDate, baby?.Name, records);
    }

    private static HaFeedingDay BuildResponse(DateTime targetDate, string? babyName, List<ChildRecord> records)
    {
        var items = new List<HaFeedItem>(records.Count);
        foreach (var r in records)
        {
            // PayloadJson 为 App 提交的 FeedRecordDto 序列化结果，取 Side/Note 等非摘要字段
            FeedRecordDto? dto = null;
            try
            {
                dto = JsonSerializer.Deserialize<FeedRecordDto>(r.PayloadJson, PayloadJsonOptions);
            }
            catch (JsonException)
            {
                // 脏数据只丢明细字段，不阻断整体响应
            }

            int? durationMin = r.DurationSec is > 0 ? (int)Math.Round(r.DurationSec.Value / 60.0) : dto?.Duration;
            items.Add(new HaFeedItem
            {
                Id = r.Id,
                Time = ToChinaIso(r.RecordTime),
                Type = r.RecordSubType ?? dto?.Type ?? string.Empty,
                Side = dto?.Side,
                DurationMin = durationMin is > 0 ? durationMin : null,
                AmountMl = r.AmountMl ?? dto?.Amount,
                Note = dto?.Note,
            });
        }

        var summary = new HaFeedSummary
        {
            Count = items.Count,
            TotalMl = items.Sum(i => i.AmountMl ?? 0),
            BreastCount = items.Count(i => i.Type == FeedType.Breast),
            BottleCount = items.Count(i => i.Type == FeedType.Bottle),
            ExpressedCount = items.Count(i => i.Type == FeedType.Expressed),
            TotalDurationMin = items.Sum(i => i.DurationMin ?? 0),
            LastFeedTime = items.Count > 0 ? items[^1].Time : null,
            LastFeedType = items.Count > 0 ? items[^1].Type : null,
        };

        return new HaFeedingDay
        {
            Date = targetDate.ToString("yyyy-MM-dd"),
            BabyName = babyName,
            Records = items,
            Summary = summary,
            ServerTime = ToChinaIsoWall(ChinaTime.Now),
        };
    }

    private static readonly TimeSpan ChinaOffset = TimeSpan.FromHours(8);

    /// <summary>UTC 存储时刻（如 RecordTime）→ 北京时间 ISO 8601（+08:00），做时区换算。</summary>
    private static string ToChinaIso(DateTime utc)
        => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToOffset(ChinaOffset)
            .ToString("yyyy-MM-dd'T'HH:mm:sszzz");

    /// <summary>北京墙钟时间（ChinaTime.Now，Kind=Unspecified）→ 附加 +08:00，不做换算。</summary>
    private static string ToChinaIsoWall(DateTime wall)
        => new DateTimeOffset(wall, ChinaOffset).ToString("yyyy-MM-dd'T'HH:mm:sszzz");
}
