using ChildNotes.Core.Dtos;
using ChildNotes.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace ChildNotes.Api.Controllers.Admin;

[ApiController]
[Route("admin/api")]
public class AdminStatsController : AdminBaseController
{
    private readonly IAdminDashboardService _dashboard;
    public AdminStatsController(IAdminDashboardService dashboard) => _dashboard = dashboard;

    [HttpGet("overview")]
    public async Task<AdminOverviewResponse> Overview(CancellationToken ct)
        => await _dashboard.GetOverviewAsync(ct);

    [HttpGet("users")]
    public async Task<AdminPageResponse<AdminUserListItemDto>> Users(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? keyword = null, CancellationToken ct = default)
        => await _dashboard.ListUsersAsync(page, pageSize, keyword, ct);

    [HttpGet("babies")]
    public async Task<AdminPageResponse<AdminBabyListItemDto>> Babies(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? keyword = null, CancellationToken ct = default)
        => await _dashboard.ListBabiesAsync(page, pageSize, keyword, ct);
}
