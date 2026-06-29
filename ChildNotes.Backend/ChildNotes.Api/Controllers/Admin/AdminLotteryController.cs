using ChildNotes.Core.Dtos;
using ChildNotes.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace ChildNotes.Api.Controllers.Admin;

[ApiController]
[Route("admin/api/lotteries")]
public class AdminLotteryController : AdminBaseController
{
    private readonly IAdminLotteryService _lottery;
    public AdminLotteryController(IAdminLotteryService lottery) => _lottery = lottery;

    [HttpGet]
    public async Task<AdminPageResponse<AdminLotteryDto>> List(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? status = null, CancellationToken ct = default)
        => await _lottery.ListLotteriesAsync(page, pageSize, status, ct);

    [HttpGet("{id:long}")]
    public async Task<AdminLotteryDto?> Get(long id, CancellationToken ct)
        => await _lottery.GetLotteryAsync(id, ct);

    [HttpPost]
    public async Task<AdminLotteryDto> Create([FromBody] AdminLotteryRequest req, CancellationToken ct)
        => await _lottery.CreateLotteryAsync(req, ct);

    [HttpPut("{id:long}")]
    public async Task<AdminLotteryDto> Update(long id, [FromBody] AdminLotteryRequest req, CancellationToken ct)
        => await _lottery.UpdateLotteryAsync(id, req, ct);

    [HttpPost("{id:long}/publish")]
    public async Task<AdminLotteryDto> Publish(long id, CancellationToken ct)
        => await _lottery.PublishLotteryAsync(id, ct);

    [HttpPost("{id:long}/close")]
    public async Task<AdminLotteryDto> Close(long id, CancellationToken ct)
        => await _lottery.CloseLotteryAsync(id, ct);
}
