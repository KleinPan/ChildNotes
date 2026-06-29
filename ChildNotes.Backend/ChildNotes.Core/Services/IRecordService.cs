using ChildNotes.Core.Dtos;
using ChildNotes.Shared.Dtos;

namespace ChildNotes.Core.Services;

public interface IRecordService
{
    Task<long> AddRecordAsync(string recordType, object dto, CancellationToken ct = default);
    Task<DailyRecordsResponse> GetTodayRecordsAsync(long? babyId, CancellationToken ct = default);
    Task<DailyRecordsResponse> GetRecordsByDateAsync(DateTime date, long? babyId, CancellationToken ct = default);
    Task<List<DailyRecordsResponse>> GetHistoryRecordsAsync(long? babyId, int limit = 30, CancellationToken ct = default);
    Task DeleteRecordAsync(long id, CancellationToken ct = default);
    Task WakeUpSleepAsync(long sleepId, CancellationToken ct = default);
}
