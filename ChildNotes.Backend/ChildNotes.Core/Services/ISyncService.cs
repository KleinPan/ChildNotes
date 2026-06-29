namespace ChildNotes.Core.Services;

public class SyncRecordItem
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public long? BabyId { get; set; }
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
    public DateTime ServerTime { get; set; }
    /// <summary>是否还有更多数据可拉取（分页用）。true 时客户端应继续请求下一页。</summary>
    public bool HasMore { get; set; }
    /// <summary>下一页游标（已拉取记录的最大 updated_at）。客户端下次请求作为 since 之外的偏移基准。</summary>
    public DateTime? NextCursor { get; set; }
}

public class SyncBabyItem
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Avatar { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public DateTime? BirthDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class SyncBatchRequest
{
    public List<SyncRecordItem> Records { get; set; } = new();
    public List<SyncBabyItem> Babies { get; set; } = new();
}

public class SyncBatchResponse
{
    public int RecordsUpserted { get; set; }
    public int BabiesUpserted { get; set; }
    public DateTime ServerTime { get; set; }
}

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
