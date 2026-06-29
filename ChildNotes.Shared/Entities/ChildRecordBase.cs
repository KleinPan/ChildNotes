namespace ChildNotes.Shared.Entities;

/// <summary>
/// 记录实体的核心字段基类（前后端共享）。
/// 前端子类追加 DeviceId/SyncedAt/GetPayload{T}；后端子类实现 IAuditable 接口。
/// </summary>
public abstract class ChildRecordBase
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
