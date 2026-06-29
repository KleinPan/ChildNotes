using ChildNotes.Shared.Sync;

namespace ChildNotes.Core.Services;

// SyncRecordItem / SyncPullResponse / SyncBabyItem / SyncBatchRequest / SyncBatchResponse
// 已迁移至 ChildNotes.Shared.Sync（前后端共享的 HTTP 协议契约）

public interface ISyncService
{
    /// <summary>
    /// 增量拉取当前用户可访问的 baby + child_record。
    /// </summary>
    /// <param name="since">增量起点（updated_at &gt; since）</param>
    /// <param name="limit">单页最大记录数（baby 与 record 各自上限）。默认 500。</param>
    /// <param name="ct"></param>
    Task<SyncPullResponse> PullAsync(DateTime since, int limit = 500, CancellationToken ct = default);
    Task<SyncBatchResponse> PushAsync(SyncBatchRequest req, CancellationToken ct = default);
}
