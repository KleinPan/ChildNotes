using System.Text.Json;
using ChildNotes.Core.Constants;
using ChildNotes.Core.Dtos;
using ChildNotes.Shared.Constants;
using ChildNotes.Shared.Dtos;
using ChildNotes.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace ChildNotes.Api.Controllers;

[Route("api/records")]
public class RecordController : AppBaseController
{
    private readonly IRecordService _record;
    public RecordController(IRecordService record) => _record = record;

    [HttpGet("today")]
    public async Task<DailyRecordsResponse> Today([FromQuery] long? babyId, CancellationToken ct)
        => await _record.GetTodayRecordsAsync(babyId, ct);

    [HttpGet("date")]
    public async Task<DailyRecordsResponse> ByDate([FromQuery] DateTime date, [FromQuery] long? babyId, CancellationToken ct)
        => await _record.GetRecordsByDateAsync(date, babyId, ct);

    [HttpGet("history")]
    public async Task<List<DailyRecordsResponse>> History([FromQuery] long? babyId, CancellationToken ct, [FromQuery] int limit = 30)
        => await _record.GetHistoryRecordsAsync(babyId, limit, ct);

    [HttpPost("{type}")]
    public async Task<object> AddRecord(string type, [FromBody] JsonElement payload, CancellationToken ct)
    {
        var dto = ParseDto(type, payload);
        var id = await _record.AddRecordAsync(type, dto, ct);
        return new { id };
    }

    [HttpDelete("{id:long}")]
    public async Task DeleteRecord(long id, CancellationToken ct)
        => await _record.DeleteRecordAsync(id, ct);

    [HttpPut("sleep/{sleepId:long}/wake")]
    public async Task WakeUpSleep(long sleepId, CancellationToken ct)
        => await _record.WakeUpSleepAsync(sleepId, ct);

    private static object ParseDto(string type, JsonElement payload)
    {
        return type switch
        {
            RecordType.Feed => payload.Deserialize<FeedRecordDto>()!,
            RecordType.Diaper => payload.Deserialize<DiaperRecordDto>()!,
            RecordType.Sleep => payload.Deserialize<SleepRecordDto>()!,
            RecordType.Temperature => payload.Deserialize<TemperatureRecordDto>()!,
            RecordType.Supplement => payload.Deserialize<SupplementRecordDto>()!,
            RecordType.Growth => payload.Deserialize<GrowthRecordDto>()!,
            RecordType.Abnormal => payload.Deserialize<AbnormalRecordDto>()!,
            RecordType.Pump => payload.Deserialize<PumpRecordDto>()!,
            RecordType.Complementary => payload.Deserialize<ComplementaryRecordDto>()!,
            RecordType.Vaccine => payload.Deserialize<VaccineRecordDto>()!,
            RecordType.Activity => payload.Deserialize<ActivityRecordDto>()!,
            RecordType.MaternalFood => payload.Deserialize<MaternalFoodRecordDto>()!,
            _ => payload,
        };
    }
}
