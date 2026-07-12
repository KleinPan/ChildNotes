using System.Text.Json;
using ChildNotes.Data.Repositories;
using ChildNotes.Infrastructure;
using ChildNotes.Models;
using ChildNotes.Shared.Dtos;
using ChildNotes.Shared.Constants;

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

    /// <summary>由 ServiceProvider 在构造完成后注入，避免循环依赖。</summary>
    public SyncTrigger? SyncTrigger { get; set; }

    /// <summary>由 ServiceProvider 在构造完成后注入，避免循环依赖。</summary>
    public ReminderService? ReminderService { get; set; }

    private void NotifyWrite()
    {
        try { SyncTrigger?.NotifyWrite(); } catch { /* 同步触发不应影响写入主流程 */ }
    }

    /// <summary>触发喂奶提醒调度：失败不影响写入主流程。</summary>
    private void TryScheduleFeedReminder(DateTime feedTime)
    {
        try { ReminderService?.ScheduleFeedReminder(feedTime); } catch { /* 提醒失败不影响记录 */ }
    }

    /// <summary>触发睡眠提醒调度：失败不影响写入主流程。</summary>
    private void TryScheduleSleepReminder(string recordId, DateTime startTime)
    {
        try { ReminderService?.ScheduleSleepReminder(recordId, startTime); } catch { /* 提醒失败不影响记录 */ }
    }

    /// <summary>取消睡眠提醒：失败不影响写入主流程。</summary>
    private void TryCancelSleepReminder(string recordId)
    {
        try { ReminderService?.CancelSleepReminder(recordId); } catch { /* 提醒失败不影响记录 */ }
    }

    public string AddFeed(FeedRecordDto dto)
    {
        var rec = NewRecord(RecordType.Feed, dto.Time);
        rec.RecordSubType = dto.Type;
        if (dto.Type == FeedType.Breast)
        {
            rec.LeftDurationSec = dto.LeftDurationSec ?? ((dto.LeftDuration ?? 0) * 60);
            rec.RightDurationSec = dto.RightDurationSec ?? ((dto.RightDuration ?? 0) * 60);
            rec.DurationSec = (rec.LeftDurationSec ?? 0) + (rec.RightDurationSec ?? 0);
        }
        else
        {
            rec.AmountMl = dto.Amount;
        }
        rec.PayloadJson = JsonSerializer.Serialize(dto);
        rec.Id = _repo.Insert(rec);
        NotifyWrite();
        // 调度喂奶间隔提醒（3 小时后）；ScheduleFeedReminder 内部会先取消旧的再重新调度
        TryScheduleFeedReminder(rec.RecordTime);
        return rec.Id;
    }

    public string AddDiaper(DiaperRecordDto dto)
    {
        var rec = NewRecord(RecordType.Diaper, dto.Time);
        rec.RecordSubType = dto.Type;
        rec.AbnormalFlag = dto.Abnormal;
        rec.PayloadJson = JsonSerializer.Serialize(dto);
        rec.Id = _repo.Insert(rec);
        NotifyWrite();
        return rec.Id;
    }

    public string AddSleep(SleepRecordDto dto)
    {
        var rec = NewRecord(RecordType.Sleep, dto.Time);
        // Duration 为空时从 StartTime/EndTime 推算（AI/规则解析可能只给起止时间未给时长）
        var duration = dto.Duration;
        if (!duration.HasValue
            && DateTime.TryParse(dto.StartTime, out var s)
            && DateTime.TryParse(dto.EndTime, out var e))
        {
            var diff = e - s;
            if (diff.TotalMinutes < 0) diff = diff.Add(TimeSpan.FromDays(1));
            duration = (int)diff.TotalMinutes;
        }
        rec.DurationSec = (duration ?? 0) * 60;
        // 同步回 dto，确保 PayloadJson 中的 Duration 与 DurationSec 一致
        if (duration.HasValue) dto.Duration = duration;
        rec.PayloadJson = JsonSerializer.Serialize(dto);
        rec.Id = _repo.Insert(rec);
        NotifyWrite();
        // 仅在未提供 Duration（即"开始睡眠"而非"补记完整睡眠"）时调度提醒
        // 若 Duration 已有值（补记已结束的睡眠），不调度提醒避免误报
        if (!duration.HasValue || duration.Value <= 0)
        {
            TryScheduleSleepReminder(rec.Id, rec.RecordTime);
        }
        return rec.Id;
    }

    public void WakeUpSleep(string recordId)
    {
        var rec = _repo.FindById(recordId);
        if (rec is null || rec.RecordType != RecordType.Sleep) return;
        var dto = rec.GetPayload<SleepRecordDto>();
        if (dto is null) return;
        var end = DateTime.Now;
        // EndTime 统一存 "HH:mm" 格式，与 SleepFormViewModel.BuildDto 保持一致，
        // 避免 FeedingViewModel.BuildSleepText 解析 ISO 格式时截取出错
        dto.EndTime = end.ToString("HH:mm");
        dto.Duration = (int)(end - rec.RecordTime).TotalMinutes;
        rec.DurationSec = dto.Duration * 60;
        rec.PayloadJson = JsonSerializer.Serialize(dto);
        _repo.Update(rec);
        NotifyWrite();
        // 睡眠结束：取消对应的睡眠超时提醒
        TryCancelSleepReminder(recordId);
    }

    public string AddTemperature(TemperatureRecordDto dto)
    {
        var rec = NewRecord(RecordType.Temperature, dto.Time);
        rec.TemperatureValue = dto.Temperature;
        rec.AbnormalFlag = dto.IsAbnormal;
        rec.PayloadJson = JsonSerializer.Serialize(dto);
        rec.Id = _repo.Insert(rec);
        NotifyWrite();
        return rec.Id;
    }

    public string AddGrowth(GrowthRecordDto dto)
    {
        var rec = NewRecord(RecordType.Growth, dto.Time);
        rec.HeightCm = dto.Height;
        rec.WeightKg = dto.Weight;
        rec.PayloadJson = JsonSerializer.Serialize(dto);
        rec.Id = _repo.Insert(rec);
        NotifyWrite();
        return rec.Id;
    }

    public string AddSupplement(SupplementRecordDto dto)
    {
        var rec = NewRecord(RecordType.Supplement, dto.Time);
        rec.RecordSubType = dto.Type;
        rec.PayloadJson = JsonSerializer.Serialize(dto);
        rec.Id = _repo.Insert(rec);
        NotifyWrite();
        return rec.Id;
    }

    public string AddWater(WaterRecordDto dto)
    {
        var rec = NewRecord(RecordType.Water, dto.Time);
        rec.AmountMl = dto.AmountMl;
        rec.PayloadJson = JsonSerializer.Serialize(dto);
        rec.Id = _repo.Insert(rec);
        NotifyWrite();
        return rec.Id;
    }

    public string AddPump(PumpRecordDto dto)
    {
        var rec = NewRecord(RecordType.Pump, dto.Time);
        rec.AmountMl = dto.TotalAmount;
        rec.LeftDurationSec = (dto.LeftDuration ?? 0) * 60;
        rec.RightDurationSec = (dto.RightDuration ?? 0) * 60;
        rec.DurationSec = (rec.LeftDurationSec ?? 0) + (rec.RightDurationSec ?? 0);
        rec.PayloadJson = JsonSerializer.Serialize(dto);
        rec.Id = _repo.Insert(rec);
        NotifyWrite();
        return rec.Id;
    }

    public string AddComplementary(ComplementaryRecordDto dto)
    {
        var rec = NewRecord(RecordType.Complementary, dto.Time);
        rec.AbnormalFlag = dto.Abnormal;
        rec.PayloadJson = JsonSerializer.Serialize(dto);
        rec.Id = _repo.Insert(rec);
        NotifyWrite();
        return rec.Id;
    }

    public string AddAbnormal(AbnormalRecordDto dto)
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
        NotifyWrite();
        return rec.Id;
    }

    public string AddVaccine(VaccineRecordDto dto)
    {
        var rec = NewRecord(RecordType.Vaccine, dto.Time);
        rec.PayloadJson = JsonSerializer.Serialize(dto);
        rec.Id = _repo.Insert(rec);
        NotifyWrite();
        return rec.Id;
    }

    /// <summary>更新已有疫苗记录（修改接种时间等）。</summary>
    public void UpdateVaccine(string id, VaccineRecordDto dto)
    {
        var rec = _repo.FindById(id);
        if (rec is null) return;
        var time = ParseTime(dto.Time);
        rec.RecordDate = time.Date;
        rec.RecordTime = time;
        rec.PayloadJson = JsonSerializer.Serialize(dto);
        _repo.Update(rec);
        NotifyWrite();
    }

    public string AddActivity(ActivityRecordDto dto)
    {
        var rec = NewRecord(RecordType.Activity, dto.Time);
        rec.RecordSubType = dto.Category;
        rec.DurationSec = (dto.Duration ?? 0) * 60;
        rec.PayloadJson = JsonSerializer.Serialize(dto);
        rec.Id = _repo.Insert(rec);
        NotifyWrite();
        return rec.Id;
    }

    public void MarkResolved(string resolvedType)
    {
        var rec = NewRecord(resolvedType, DateTime.Now.ToString("O"));
        rec.PayloadJson = "{}";
        _repo.Insert(rec);
        NotifyWrite();
    }

    public List<ChildRecord> GetByDate(DateTime date) => _repo.GetByDate(_state.UserId, _state.CurrentBabyId, date);
    public List<ChildRecord> GetByDateRange(DateTime start, DateTime end) => _repo.GetByDateRange(_state.UserId, _state.CurrentBabyId, start, end);
    public ChildRecord? GetLatest(string type) => _repo.GetLatest(_state.UserId, _state.CurrentBabyId, type);
    public List<ChildRecord> GetByType(string type, int limit = 100) => _repo.GetByType(_state.UserId, _state.CurrentBabyId, type, limit);
    public void Delete(string id)
    {
        // 删除前查询记录类型，用于取消对应的提醒
        var rec = _repo.FindById(id);
        _repo.SoftDelete(id);
        NotifyWrite();
        if (rec is null) return;
        // 删除喂奶记录：取消喂奶提醒（避免删除后仍提醒"该喂奶了"）
        if (rec.RecordType == RecordType.Feed)
        {
            try { ReminderService?.CancelFeedReminder(); } catch { /* 提醒取消失败不影响删除 */ }
        }
        // 删除睡眠记录：取消对应的睡眠提醒
        else if (rec.RecordType == RecordType.Sleep)
        {
            TryCancelSleepReminder(id);
        }
    }
    public ChildRecord? GetById(string id) => _repo.FindById(id);
    public void Update(ChildRecord rec) { _repo.Update(rec); NotifyWrite(); }

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
