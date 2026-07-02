namespace ChildNotes.Shared.Sync;

/// <summary>
/// 同步协议 DTO。前后端共享，确保 HTTP /api/sync/* 接口契约一致。
/// 修改此文件需同步构建前后端。
/// </summary>
public class SyncRecordItem
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? BabyId { get; set; }
    public string RecordType { get; set; } = string.Empty;
    public string? RecordSubType { get; set; }
    public DateTime RecordDate { get; set; }
    public DateTime RecordTime { get; set; }
    public int? AmountMl { get; set; }
    public int? DurationSec { get; set; }
    public int? LeftDurationSec { get; set; }
    public int? RightDurationSec { get; set; }
    public bool? AbnormalFlag { get; set; }
    public decimal? TemperatureValue { get; set; }
    public decimal? HeightCm { get; set; }
    public decimal? WeightKg { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public bool Deleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class SyncPullResponse
{
    public List<SyncRecordItem> Records { get; set; } = new();
    public List<SyncBabyItem> Babies { get; set; } = new();
    public List<SyncMilestoneItem> Milestones { get; set; } = new();
    public DateTime ServerTime { get; set; }
    /// <summary>是否还有更多数据可拉取（分页用）。true 时客户端应继续请求下一页。</summary>
    public bool HasMore { get; set; }
    /// <summary>下一页游标（已拉取记录的最大 updated_at）。客户端下次请求作为 since 之外的偏移基准。</summary>
    public DateTime? NextCursor { get; set; }
}

/// <summary>
/// 成长时刻（里程碑）同步项。与 <see cref="SyncRecordItem"/> 平行，独立同步通道。
/// PhotosJson 透传图片 URL 数组字符串，前后端不解析。
/// </summary>
public class SyncMilestoneItem
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? BabyId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Content { get; set; }
    public DateTime RecordDate { get; set; }
    public string PhotosJson { get; set; } = "[]";
    public bool Deleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class SyncBabyItem
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Avatar { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public DateTime? BirthDate { get; set; }
    /// <summary>软删除标记：true 表示已删除。多设备同步时传递此字段以避免删除"复活"。</summary>
    public bool Deleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class SyncBatchRequest
{
    public List<SyncRecordItem> Records { get; set; } = new();
    public List<SyncBabyItem> Babies { get; set; } = new();
    public List<SyncMilestoneItem> Milestones { get; set; } = new();
}

public class SyncBatchResponse
{
    public int RecordsUpserted { get; set; }
    public int BabiesUpserted { get; set; }
    public int MilestonesUpserted { get; set; }
    public DateTime ServerTime { get; set; }
}
