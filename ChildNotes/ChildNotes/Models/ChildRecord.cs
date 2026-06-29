using System.Text.Json;

namespace ChildNotes.Models;

public sealed class ChildRecord
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

    /// <summary>创建该记录的设备标识（用于多设备冲突归因）。</summary>
    public string? DeviceId { get; set; }
    /// <summary>最后一次成功上送到服务器的时间；null 表示尚未上送（待发）。</summary>
    public DateTime? SyncedAt { get; set; }

    public T? GetPayload<T>() => string.IsNullOrEmpty(PayloadJson)
        ? default
        : JsonSerializer.Deserialize<T>(PayloadJson);
}
