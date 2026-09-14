using System.Text.Json;
using ChildNotes.Core.Constants;
using ChildNotes.Core.Dtos;
using ChildNotes.Shared.Constants;
using ChildNotes.Shared.Dtos;
using ChildNotes.Core.Entities;
using ChildNotes.Core.Exceptions;
using ChildNotes.Core.Services;
using ChildNotes.Shared.Services;
using ChildNotes.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChildNotes.Infrastructure.Services;

public class RecordService : IRecordService
{
    private readonly ChildNotesDbContext _db;
    private readonly ICurrentUserService _current;
    private readonly IBabyAccessService _babyAccess;
    private readonly IFamilyService _familyService;

    public RecordService(ChildNotesDbContext db, ICurrentUserService current, IBabyAccessService babyAccess,
        IFamilyService familyService)
    {
        _db = db;
        _current = current;
        _babyAccess = babyAccess;
        _familyService = familyService;
    }

    public async Task<string> AddRecordAsync(string recordType, object dto, CancellationToken ct = default)
    {
        if (!RecordType.All.Contains(recordType))
            throw new BusinessException($"不支持的记录类型: {recordType}", 400, "UNSUPPORTED_RECORD_TYPE");

        var uid = _current.RequireUserId();
        var baby = await _babyAccess.GetDefaultBabyAsync(uid, ct);
        // Family-centric：记录归属跟随 baby 的家庭（baby 来自当前家庭默认宝宝）
        var familyId = baby?.FamilyId ?? await _familyService.GetCurrentFamilyIdAsync(uid, ct) ?? string.Empty;
        var time = ExtractTime(dto);
        var rec = new ChildRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = uid,
            FamilyId = familyId,
            BabyId = baby?.Id,
            RecordType = recordType,
            RecordDate = DateTime.SpecifyKind(time.Date, DateTimeKind.Utc),
            RecordTime = DateTime.SpecifyKind(time, DateTimeKind.Utc),
            PayloadJson = JsonSerializer.Serialize(dto, dto.GetType()),
        };
        FillSummaryFields(rec, recordType, dto);
        _db.ChildRecords.Add(rec);
        await _db.SaveChangesAsync(ct);
        return rec.Id;
    }

    public async Task<DailyRecordsResponse> GetTodayRecordsAsync(string? babyId, CancellationToken ct = default)
        => await GetRecordsByDateAsync(DateTime.Today, babyId, ct);

    public async Task<DailyRecordsResponse> GetRecordsByDateAsync(DateTime date, string? babyId, CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        var targetBabyId = await ResolveBabyIdAsync(uid, babyId, ct);
        var dateOnly = date.Date;
        var records = await _db.ChildRecords
            .AsNoTracking()
            .Where(r => r.UserId == uid && r.BabyId == targetBabyId && r.RecordDate == dateOnly)
            .OrderBy(r => r.RecordTime).ToListAsync(ct);
        return BuildDailyResponse(dateOnly, records);
    }

    public async Task<List<DailyRecordsResponse>> GetHistoryRecordsAsync(string? babyId, int limit = 30, CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        var targetBabyId = await ResolveBabyIdAsync(uid, babyId, ct);
        var dates = await _db.ChildRecords
            .Where(r => r.UserId == uid && r.BabyId == targetBabyId)
            .Select(r => r.RecordDate).Distinct().OrderByDescending(d => d)
            .Take(limit).ToListAsync(ct);
        if (dates.Count == 0) return new List<DailyRecordsResponse>();

        // 一次取回全部目标日期的记录（WHERE RecordDate IN (...)），消除逐日查询的 N+1；
        // 全局按 RecordTime 排序后分组，组内相对顺序即各日期内的 RecordTime 升序，排序语义与逐日查询一致
        var records = await _db.ChildRecords
            .AsNoTracking()
            .Where(r => r.UserId == uid && r.BabyId == targetBabyId && dates.Contains(r.RecordDate))
            .OrderBy(r => r.RecordTime).ToListAsync(ct);
        var recordsByDate = records.GroupBy(r => r.RecordDate).ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<DailyRecordsResponse>();
        foreach (var d in dates)
        {
            result.Add(BuildDailyResponse(d, recordsByDate.GetValueOrDefault(d) ?? new List<ChildRecord>()));
        }
        return result;
    }

    public async Task DeleteRecordAsync(string id, CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        var rec = await _db.ChildRecords.FirstOrDefaultAsync(r => r.Id == id && r.UserId == uid, ct)
            ?? throw new NotFoundException("记录不存在");
        rec.Deleted = true;
        await _db.SaveChangesAsync(ct);
    }

    public async Task WakeUpSleepAsync(string sleepId, CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        var rec = await _db.ChildRecords.FirstOrDefaultAsync(
            r => r.Id == sleepId && r.UserId == uid && r.RecordType == RecordType.Sleep, ct)
            ?? throw new NotFoundException("睡眠记录不存在");
        var dto = JsonSerializer.Deserialize<SleepRecordDto>(rec.PayloadJson)
            ?? throw new BusinessException("记录解析失败");
        var end = ChinaTime.Now;
        dto.EndTime = end.ToString("O");
        dto.Duration = (int)(end - rec.RecordTime).TotalMinutes;
        rec.DurationSec = dto.Duration * 60;
        rec.PayloadJson = JsonSerializer.Serialize(dto);
        await _db.SaveChangesAsync(ct);
    }

    private static void FillSummaryFields(ChildRecord rec, string recordType, object dto)
    {
        // 直接从强类型 dto 取摘要字段，避免"序列化 PayloadJson 后又反序列化同一 JSON"的往返开销；
        // 落库的 PayloadJson 仍由 AddRecordAsync 序列化 dto 生成，内容不变。
        // 控制器 ParseDto 保证 recordType 与 dto 运行时类型一一对应，模式匹配失败仅静默跳过（防御）。
        switch (recordType)
        {
            case RecordType.Feed:
                if (dto is FeedRecordDto f)
                {
                    rec.RecordSubType = f.Type;
                    if (f.Type == FeedType.Breast)
                    {
                        rec.LeftDurationSec = f.LeftDurationSec ?? (f.LeftDuration ?? 0) * 60;
                        rec.RightDurationSec = f.RightDurationSec ?? (f.RightDuration ?? 0) * 60;
                        rec.DurationSec = (rec.LeftDurationSec ?? 0) + (rec.RightDurationSec ?? 0);
                    }
                    else
                    {
                        rec.AmountMl = f.Amount;
                    }
                }
                break;
            case RecordType.Diaper:
                if (dto is DiaperRecordDto d)
                {
                    rec.RecordSubType = d.Type;
                    rec.AbnormalFlag = d.Abnormal;
                }
                break;
            case RecordType.Sleep:
                if (dto is SleepRecordDto s)
                {
                    rec.DurationSec = (s.Duration ?? 0) * 60;
                }
                break;
            case RecordType.Temperature:
                if (dto is TemperatureRecordDto t)
                {
                    rec.TemperatureValue = t.Temperature;
                    rec.AbnormalFlag = t.IsAbnormal || t.Temperature >= HealthConstants.FeverThreshold;
                }
                break;
            case RecordType.Growth:
                if (dto is GrowthRecordDto g)
                {
                    rec.HeightCm = g.Height;
                    rec.WeightKg = g.Weight;
                }
                break;
            case RecordType.Abnormal:
                if (dto is AbnormalRecordDto a)
                {
                    rec.TemperatureValue = a.Temperature;
                    rec.AbnormalFlag = true;
                }
                break;
            case RecordType.Pump:
                if (dto is PumpRecordDto p)
                {
                    rec.AmountMl = p.TotalAmount;
                    rec.LeftDurationSec = p.LeftDuration;
                    rec.RightDurationSec = p.RightDuration;
                }
                break;
            case RecordType.Complementary:
                if (dto is ComplementaryRecordDto c)
                {
                    rec.AbnormalFlag = c.Abnormal;
                }
                break;
        }
    }

    private static DateTime ExtractTime(object dto)
    {
        // 睡眠记录：优先用 StartTime（小程序不填 Time 字段，只用 StartTime/EndTime）
        // 原因：小程序 sleep-form 提交时 data 只有 startTime/endTime/duration/note，无 time 字段，
        // 若回退到 DateTime.Now 会导致睡眠记录排序错乱（RecordTime 为创建时刻而非睡眠开始时间）
        if (dto is SleepRecordDto sleep && !string.IsNullOrWhiteSpace(sleep.StartTime))
        {
            if (DateTime.TryParse(sleep.StartTime, out var st)) return st;
            // StartTime 是 "HH:mm" 格式时，结合当天日期
            if (TimeSpan.TryParse(sleep.StartTime, out var tp))
                return ChinaTime.Today + tp;
        }

        var timeProp = dto.GetType().GetProperty("Time");
        var timeStr = timeProp?.GetValue(dto)?.ToString();
        if (string.IsNullOrEmpty(timeStr)) return ChinaTime.Now;
        if (DateTime.TryParse(timeStr, out var t)) return t;
        return ChinaTime.Now;
    }

    private async Task<string?> ResolveBabyIdAsync(string userId, string? babyId, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(babyId))
        {
            await _babyAccess.EnsureAccessAsync(userId, babyId, ct);
            return babyId;
        }
        var baby = await _babyAccess.GetDefaultBabyAsync(userId, ct);
        return baby?.Id;
    }

    private static DailyRecordsResponse BuildDailyResponse(DateTime date, List<ChildRecord> records)
    {
        var resp = new DailyRecordsResponse { Date = date };
        foreach (var r in records)
        {
            if (!resp.RecordsByType.TryGetValue(r.RecordType, out var list))
            {
                list = new List<JsonElement>();
                resp.RecordsByType[r.RecordType] = list;
            }
            using var doc = JsonDocument.Parse(r.PayloadJson);
            list.Add(doc.RootElement.Clone());
        }
        return resp;
    }
}
