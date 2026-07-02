using ChildNotes.Shared.Entities;

namespace ChildNotes.Models;

public sealed class UserPoints : UserPointsBase
{
}

public sealed class SignInRecord
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public DateTime SignDate { get; set; }
    public int ContinuousDays { get; set; }
    public int Reward { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class TaskRecord
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string TaskCode { get; set; } = string.Empty;
    public string TaskName { get; set; } = string.Empty;
    public int Reward { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 成长时刻（里程碑）实体。继承共享基类 + 同步元数据。
/// PhotosJson 存储图片 URL/路径的 JSON 数组字符串。
/// </summary>
public sealed class Milestone : MilestoneBase
{
    /// <summary>软删标记（同步用）。true 表示已删除。</summary>
    public bool Deleted { get; set; }
    /// <summary>创建该里程碑的设备标识（多设备冲突归因用）。</summary>
    public string? DeviceId { get; set; }
    /// <summary>最后一次成功上送到服务器的时间；null 表示尚未上送。</summary>
    public DateTime? SyncedAt { get; set; }
}
