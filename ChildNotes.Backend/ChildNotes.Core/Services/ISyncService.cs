using ChildNotes.Shared.Sync;

namespace ChildNotes.Core.Services;

// SyncRecordItem / SyncPullResponse / SyncBabyItem / SyncBatchRequest / SyncBatchResponse
// 已迁移至 ChildNotes.Shared.Sync（前后端共享的 HTTP 协议契约）

public interface ISyncService
{
    /// <summary>
    /// 增量拉取当前用户可访问的 baby + child_record + milestone + baby_member。
    /// 使用复合游标 (UpdatedAt, Id) 精确分页，防止同一 UpdatedAt 跨页漏数据。
    /// </summary>
    /// <param name="since">增量起点（updated_at &gt; since）</param>
    /// <param name="limit">单页最大记录数（每类各自上限）。默认 500，实现会 Clamp 到 [1, 2000]。</param>
    /// <param name="cursorTime">上一页复合游标的时间戳（null 表示第一页）</param>
    /// <param name="cursorId">上一页复合游标的 Id（null 表示第一页）</param>
    /// <param name="ct"></param>
    Task<SyncPullResponse> PullAsync(DateTime since, int limit = 500,
        DateTime? cursorTime = null, string? cursorId = null, CancellationToken ct = default);
    Task<SyncBatchResponse> PushAsync(SyncBatchRequest req, CancellationToken ct = default);
}
