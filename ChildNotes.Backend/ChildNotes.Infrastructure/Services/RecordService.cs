using System.Text.Json;
using ChildNotes.Core.Constants;
using ChildNotes.Core.Dtos;
using ChildNotes.Core.Entities;
using ChildNotes.Core.Exceptions;
using ChildNotes.Core.Services;
using ChildNotes.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChildNotes.Infrastructure.Services;

public class RecordService : IRecordService
{
    private const decimal FeverThreshold = 37.3m;
    private readonly ChildNotesDbContext _db;
    private readonly ICurrentUserService _current;
    private readonly IBabyAccessService _babyAccess;

    public RecordService(ChildNotesDbContext db, ICurrentUserService current, IBabyAccessService babyAccess)
    {
        _db = db;
        _current = current;
        _babyAccess = babyAccess;
    }

    public async Task<long> AddRecordAsync(string recordType, object dto, CancellationToken ct = default)
    {
        if (!RecordType.All.Contains(recordType))
            throw new BusinessException($"不支持的记录类型: {recordType}", 400, "UNSUPPORTED_RECORD_TYPE");

        var uid = _current.RequireUserId();
        var baby = await _babyAccess.GetDefaultBabyAsync(uid, ct);
        var time = ExtractTime(dto);
        var rec = new ChildRecord
        {
            UserId = uid,
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

    public async Task<DailyRecordsResponse> GetTodayRecordsAsync(long? babyId, CancellationToken ct = default)
        => await GetRecordsByDateAsync(DateTime.Today, babyId, ct);

    public async Task<DailyRecordsResponse> GetRecordsByDateAsync(DateTime date, long? babyId, CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        var targetBabyId = await ResolveBabyIdAsync(uid, babyId, ct);
        var dateOnly = date.Date;
        var records = await _db.ChildRecords
            .Where(r => r.UserId == uid && r.BabyId == targetBabyId && r.RecordDate == dateOnly)
            .OrderBy(r => r.RecordTime).ToListAsync(ct);
        return BuildDailyResponse(dateOnly, records);
    }

    public async Task<List<DailyRecordsResponse>> GetHistoryRecordsAsync(long? babyId, int limit = 30, CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        var targetBabyId = await ResolveBabyIdAsync(uid, babyId, ct);
        var dates = await _db.ChildRecords
            .Where(r => r.UserId == uid && r.BabyId == targetBabyId)
            .Select(r => r.RecordDate).Distinct().OrderByDescending(d => d)
            .Take(limit).ToListAsync(ct);
        var result = new List<DailyRecordsResponse>();
        foreach (var d in dates)
        {
            var records = await _db.ChildRecords
                .Where(r => r.UserId == uid && r.BabyId == targetBabyId && r.RecordDate == d)
                .OrderBy(r => r.RecordTime).ToListAsync(ct);
            result.Add(BuildDailyResponse(d, records));
        }
        return result;
    }

    public async Task DeleteRecordAsync(long id, CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        var rec = await _db.ChildRecords.FirstOrDefaultAsync(r => r.Id == id && r.UserId == uid, ct)
            ?? throw new NotFoundException("记录不存在");
        rec.Deleted = true;
        await _db.SaveChangesAsync(ct);
    }

    public async Task WakeUpSleepAsync(long sleepId, CancellationToken ct = default)
    {
        var uid = _current.RequireUserId();
        var rec = await _db.ChildRecords.FirstOrDefaultAsync(
            r => r.Id == sleepId && r.UserId == uid && r.RecordType == RecordType.Sleep, ct)
            ?? throw new NotFoundException("睡眠记录不存在");
        var dto = JsonSerializer.Deserialize<SleepRecordDto>(rec.PayloadJson)
            ?? throw new BusinessException("记录解析失败");
        var end = DateTime.Now;
        dto.EndTime = end.ToString("O");
        dto.Duration = (int)(end - rec.RecordTime).TotalMinutes;
        rec.DurationSec = dto.Duration * 60;
        rec.PayloadJson = JsonSerializer.Serialize(dto);
        await _db.SaveChangesAsync(ct);
    }

    private void FillSummaryFields(ChildRecord rec, string recordType, object dto)
    {
        var json = rec.PayloadJson;
        switch (recordType)
        {
            case RecordType.Feed:
                var f = JsonSerializer.Deserialize<FeedRecordDto>(json)!;
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
                break;
            case RecordType.Diaper:
                var d = JsonSerializer.Deserialize<DiaperRecordDto>(json)!;
                rec.RecordSubType = d.Type;
                rec.AbnormalFlag = d.Abnormal;
                break;
            case RecordType.Sleep:
                var s = JsonSerializer.Deserialize<SleepRecordDto>(json)!;
                rec.DurationSec = (s.Duration ?? 0) * 60;
                break;
            case RecordType.Temperature:
                var t = JsonSerializer.Deserialize<TemperatureRecordDto>(json)!;
                rec.TemperatureValue = t.Temperature;
                rec.AbnormalFlag = t.IsAbnormal || t.Temperature >= FeverThreshold;
                break;
            case RecordType.Growth:
                var g = JsonSerializer.Deserialize<GrowthRecordDto>(json)!;
                rec.HeightCm = g.Height;
                rec.WeightKg = g.Weight;
                break;
            case RecordType.Abnormal:
                var a = JsonSerializer.Deserialize<AbnormalRecordDto>(json)!;
                rec.TemperatureValue = a.Temperature;
                rec.AbnormalFlag = true;
                break;
            case RecordType.Pump:
                var p = JsonSerializer.Deserialize<PumpRecordDto>(json)!;
                rec.AmountMl = p.TotalAmount;
                rec.LeftDurationSec = p.LeftDuration;
                rec.RightDurationSec = p.RightDuration;
                break;
            case RecordType.Complementary:
                var c = JsonSerializer.Deserialize<ComplementaryRecordDto>(json)!;
                rec.AbnormalFlag = c.Abnormal;
                break;
        }
    }

    private static DateTime ExtractTime(object dto)
    {
        var timeProp = dto.GetType().GetProperty("Time");
        var timeStr = timeProp?.GetValue(dto)?.ToString();
        if (string.IsNullOrEmpty(timeStr)) return DateTime.Now;
        if (DateTime.TryParse(timeStr, out var t)) return t;
        return DateTime.Now;
    }

    private async Task<long?> ResolveBabyIdAsync(long userId, long? babyId, CancellationToken ct)
    {
        if (babyId.HasValue)
        {
            await _babyAccess.EnsureAccessAsync(userId, babyId.Value, ct);
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
