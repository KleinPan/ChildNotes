using ChildNotes.Core.Dtos;
using ChildNotes.Shared.Dtos;

namespace ChildNotes.Core.Services;

public interface IRecordService
{
    Task<string> AddRecordAsync(string recordType, object dto, CancellationToken ct = default);
    Task<DailyRecordsResponse> GetTodayRecordsAsync(string? babyId, CancellationToken ct = default);
    Task<DailyRecordsResponse> GetRecordsByDateAsync(DateTime date, string? babyId, CancellationToken ct = default);
    Task<List<DailyRecordsResponse>> GetHistoryRecordsAsync(string? babyId, int limit = 30, CancellationToken ct = default);
    Task DeleteRecordAsync(string id, CancellationToken ct = default);
    Task WakeUpSleepAsync(string sleepId, CancellationToken ct = default);
}
