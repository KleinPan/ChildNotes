namespace ChildNotes.Models;

/// <summary>
/// 同步日志条目：记录每次同步的时间、状态、数据量、结果描述。
/// 持久化到 sync_log 表（保留最近 10 条），用于在数据同步页底部展示。
/// </summary>
public sealed class SyncLogEntry
{
    /// <summary>数据库自增主键。</summary>
    public long Id { get; set; }

    /// <summary>同步完成时间（本地时间，精确到秒）。</summary>
    public DateTime DoneAt { get; set; }

    /// <summary>
    /// 同步状态：<c>success</c> / <c>failed</c> / <c>running</c>。
    /// 与 SyncConfig.LastSyncStatus 不同，这里多了 running 中间态。
    /// </summary>
    public string Status { get; set; } = "failed";

    /// <summary>同步数据量描述（如 "拉取 3宝/12条；推送 1宝/2条"）。</summary>
    public string DataVolume { get; set; } = string.Empty;

    /// <summary>简短的同步结果描述（成功时为汇总，失败时为错误原因）。</summary>
    public string Message { get; set; } = string.Empty;
}
