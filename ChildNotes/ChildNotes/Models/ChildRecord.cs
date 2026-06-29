using System.Text.Json;
using ChildNotes.Shared.Entities;

namespace ChildNotes.Models;

public sealed class ChildRecord : ChildRecordBase
{
    /// <summary>创建该记录的设备标识（用于多设备冲突归因）。</summary>
    public string? DeviceId { get; set; }
    /// <summary>最后一次成功上送到服务器的时间；null 表示尚未上送（待发）。</summary>
    public DateTime? SyncedAt { get; set; }

    public T? GetPayload<T>() => string.IsNullOrEmpty(PayloadJson)
        ? default
        : JsonSerializer.Deserialize<T>(PayloadJson);
}
