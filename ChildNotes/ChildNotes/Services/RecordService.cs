using System.Text.Json;
using ChildNotes.Data.Repositories;
using ChildNotes.Models;
using ChildNotes.Models.Dtos;

namespace ChildNotes.Services;

public sealed class RecordService
{
    private readonly RecordRepository _repo;
    private readonly AppState _state;

    public RecordService(RecordRepository repo, AppState state)
    {
        _repo = repo;
        _state = state;
    }

    public long AddFeed(FeedRecordDto dto)
    {
        var rec = NewRecord(RecordType.Feed, dto.Time);
        rec.RecordSubType = dto.Type;
        if (dto.Type == FeedType.Breast)
        {
            rec.LeftDurationSec = dto.LeftDurationSec ?? (dto.LeftDuration * 60);
            rec.RightDurationSec = dto.RightDurationSec ?? (dto.RightDuration * 60);
            rec.DurationSec = rec.LeftDurationSec + rec.RightDurationSec;
        }
        else
        {
            rec.AmountMl = dto.Amount;
        }
        rec.PayloadJson = JsonSerializer.Serialize(dto);
        rec.Id = _repo.Insert(rec);
        return rec.Id;
    }

    public long AddDiaper(DiaperRecordDto dto)
    {
        var rec = NewRecord(RecordType.Diaper, dto.Time);
        rec.RecordSubType = dto.Type;
        rec.AbnormalFlag = dto.Abnormal;
        rec.PayloadJson = JsonSerializer.Serialize(dto);
        rec.Id = _repo.Insert(rec);
        return rec.Id;
    }

    public long AddSleep(SleepRecordDto dto)
    {
        var rec = NewRecord(RecordType.Sleep, dto.Time);
        rec.DurationSec = dto.Duration * 60;
        rec.PayloadJson = JsonSerializer.Serialize(dto);
        rec.Id = _repo.Insert(rec);
        return rec.Id;
    }

    public void WakeUpSleep(long recordId)
    {
        var rec = _repo.FindById(recordId);
        if (rec is null || rec.RecordType != RecordType.Sleep) return;
        var dto = rec.GetPayload<SleepRecordDto>()!;
        var end = DateTime.Now;
        dto.EndTime = end.ToString("O");
        dto.Duration = (int)(end - rec.RecordTime).TotalMinutes;
        rec.DurationSec = dto.Duration * 60;
        rec.PayloadJson = JsonSerializer.Serialize(dto);
        _repo.Update(rec);
    }

    public long AddTemperature(TemperatureRecordDto dto)
    {
        var rec = NewRecord(RecordType.Temperature, dto.Time);
        rec.TemperatureValue = dto.Temperature;
        rec.AbnormalFlag = dto.IsAbnormal;
        rec.PayloadJson = JsonSerializer.Serialize(dto);
        rec.Id = _repo.Insert(rec);
        return rec.Id;
    }

    public long AddGrowth(GrowthRecordDto dto)
    {
        var rec = NewRecord(RecordType.Growth, dto.Time);
        rec.HeightCm = dto.Height;
        rec.WeightKg = dto.Weight;
        rec.PayloadJson = JsonSerializer.Serialize(dto);
        rec.Id = _repo.Insert(rec);
        return rec.Id;
    }

    public long AddSupplement(SupplementRecordDto dto)
    {
        var rec = NewRecord(RecordType.Supplement, dto.Time);
        rec.RecordSubType = dto.Type;
        rec.PayloadJson = JsonSerializer.Serialize(dto);
        rec.Id = _repo.Insert(rec);
        return rec.Id;
    }

    public long AddPump(PumpRecordDto dto)
    {
        var rec = NewRecord(RecordType.Pump, dto.Time);
        rec.AmountMl = dto.TotalAmount;
        rec.LeftDurationSec = dto.LeftDuration * 60;
        rec.RightDurationSec = dto.RightDuration * 60;
        rec.DurationSec = rec.LeftDurationSec + rec.RightDurationSec;
        rec.PayloadJson = JsonSerializer.Serialize(dto);
        rec.Id = _repo.Insert(rec);
        return rec.Id;
    }

    public long AddComplementary(ComplementaryRecordDto dto)
    {
        var rec = NewRecord(RecordType.Complementary, dto.Time);
        rec.AbnormalFlag = dto.Abnormal;
        rec.PayloadJson = JsonSerializer.Serialize(dto);
        rec.Id = _repo.Insert(rec);
        return rec.Id;
    }

    public long AddAbnormal(AbnormalRecordDto dto)
    {
        var rec = NewRecord(RecordType.Abnormal, dto.Time);
        rec.TemperatureValue = dto.Temperature;
        rec.AbnormalFlag = true;
        if (dto.Temperature.HasValue && dto.Temperature.Value >= 38m)
            rec.RecordSubType = "fever";
        else if (dto.Diarrhea.Count > 0)
            rec.RecordSubType = "diarrhea";
        else if (dto.Vomit)
            rec.RecordSubType = "vomit";
        else if (dto.Medicine)
            rec.RecordSubType = "medicine";
        rec.PayloadJson = JsonSerializer.Serialize(dto);
        rec.Id = _repo.Insert(rec);
        return rec.Id;
    }

    public long AddVaccine(VaccineRecordDto dto)
    {
        var rec = NewRecord(RecordType.Vaccine, dto.Time);
        rec.PayloadJson = JsonSerializer.Serialize(dto);
        rec.Id = _repo.Insert(rec);
        return rec.Id;
    }

    public long AddActivity(ActivityRecordDto dto)
    {
        var rec = NewRecord(RecordType.Activity, dto.Time);
        rec.RecordSubType = dto.Category;
        rec.DurationSec = dto.Duration * 60;
        rec.PayloadJson = JsonSerializer.Serialize(dto);
        rec.Id = _repo.Insert(rec);
        return rec.Id;
    }

    public void MarkResolved(string resolvedType)
    {
        var rec = NewRecord(resolvedType, DateTime.Now.ToString("O"));
        rec.PayloadJson = "{}";
        _repo.Insert(rec);
    }

    public List<ChildRecord> GetByDate(DateTime date) => _repo.GetByDate(_state.UserId, _state.CurrentBabyId, date);
    public List<ChildRecord> GetByDateRange(DateTime start, DateTime end) => _repo.GetByDateRange(_state.UserId, _state.CurrentBabyId, start, end);
    public ChildRecord? GetLatest(string type) => _repo.GetLatest(_state.UserId, _state.CurrentBabyId, type);
    public List<ChildRecord> GetByType(string type, int limit = 100) => _repo.GetByType(_state.UserId, _state.CurrentBabyId, type, limit);
    public void Delete(long id) => _repo.SoftDelete(id);
    public ChildRecord? GetById(long id) => _repo.FindById(id);
    public void Update(ChildRecord rec) => _repo.Update(rec);

    private ChildRecord NewRecord(string type, string timeStr)
    {
        var time = ParseTime(timeStr);
        return new ChildRecord
        {
            UserId = _state.UserId,
            BabyId = _state.CurrentBabyId,
            RecordType = type,
            RecordDate = time.Date,
            RecordTime = time,
        };
    }

    private static DateTime ParseTime(string timeStr)
    {
        if (string.IsNullOrEmpty(timeStr)) return DateTime.Now;
        return DateTime.TryParse(timeStr, out var t) ? t : DateTime.Now;
    }
}
