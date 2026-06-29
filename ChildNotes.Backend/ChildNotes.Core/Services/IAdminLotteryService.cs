using ChildNotes.Core.Dtos;

namespace ChildNotes.Core.Services;

public interface IAdminLotteryService
{
    Task<AdminPageResponse<AdminLotteryDto>> ListLotteriesAsync(int page, int pageSize, string? status, CancellationToken ct = default);
    Task<AdminLotteryDto?> GetLotteryAsync(long id, CancellationToken ct = default);
    Task<AdminLotteryDto> CreateLotteryAsync(AdminLotteryRequest req, CancellationToken ct = default);
    Task<AdminLotteryDto> UpdateLotteryAsync(long id, AdminLotteryRequest req, CancellationToken ct = default);
    Task<AdminLotteryDto> PublishLotteryAsync(long id, CancellationToken ct = default);
    Task<AdminLotteryDto> CloseLotteryAsync(long id, CancellationToken ct = default);
}
