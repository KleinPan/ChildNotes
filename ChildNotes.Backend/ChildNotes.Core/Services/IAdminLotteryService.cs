using ChildNotes.Core.Dtos;

namespace ChildNotes.Core.Services;

public interface IAdminLotteryService
{
    Task<AdminPageResponse<AdminLotteryDto>> ListLotteriesAsync(int page, int pageSize, string? status, CancellationToken ct = default);
    Task<AdminLotteryDto?> GetLotteryAsync(string id, CancellationToken ct = default);
    Task<AdminLotteryDto> CreateLotteryAsync(AdminLotteryRequest req, CancellationToken ct = default);
    Task<AdminLotteryDto> UpdateLotteryAsync(string id, AdminLotteryRequest req, CancellationToken ct = default);
    Task<AdminLotteryDto> PublishLotteryAsync(string id, CancellationToken ct = default);
    Task<AdminLotteryDto> CloseLotteryAsync(string id, CancellationToken ct = default);
}
