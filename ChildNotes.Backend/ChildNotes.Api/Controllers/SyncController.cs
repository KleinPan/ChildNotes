using ChildNotes.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace ChildNotes.Api.Controllers;

[Route("api/sync")]
public class SyncController : AppBaseController
{
    private readonly ISyncService _sync;
    public SyncController(ISyncService sync) => _sync = sync;

    [HttpGet("pull")]
    public async Task<SyncPullResponse> Pull([FromQuery] DateTime? since, [FromQuery] int? limit, CancellationToken ct)
        => await _sync.PullAsync(since ?? DateTime.UnixEpoch, limit ?? 500, ct);

    [HttpPost("push")]
    public async Task<SyncBatchResponse> Push([FromBody] SyncBatchRequest req, CancellationToken ct)
        => await _sync.PushAsync(req, ct);
}
