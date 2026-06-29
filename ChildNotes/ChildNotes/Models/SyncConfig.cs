namespace ChildNotes.Models;

public sealed class SyncConfig
{
    public int Id { get; set; } = 1;
    public bool Enabled { get; set; }
    public string ServerUrl { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public DateTime? LastSyncAt { get; set; }
    public string? LastSyncStatus { get; set; }
    public string? LastSyncMsg { get; set; }

    /// <summary>设备唯一标识（首次启动生成，用于冲突归因与 device_id 字段）。</summary>
    public string DeviceId { get; set; } = string.Empty;
}
