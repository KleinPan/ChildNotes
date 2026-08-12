using ChildNotes.Infrastructure;
using ChildNotes.Shared.Dtos;
using ChildNotes.Shared.Entities;

namespace ChildNotes.Models;

public sealed class Baby : BabyBase
{
    /// <summary>创建该宝宝的设备标识（用于多设备冲突归因）。</summary>
    public string? DeviceId { get; set; }
    /// <summary>最后一次成功上送到服务器的时间；null 表示尚未上送（待发）。</summary>
    public DateTime? SyncedAt { get; set; }

    /// <summary>
    /// 该宝宝的家庭成员信息（在线数据，非持久化字段）。
    /// 由 BabyManagerViewModel.LoadAsync 拉取后填充，供"人员管理"页 UI 绑定。
    /// 不写入 SQLite（BabyRepository 用显式 SQL 字段映射，新增属性不会被持久化）。
    /// </summary>
    public BabyFamilyDto? Family { get; set; }

    public int AgeInDays => BirthDate.HasValue ? (int)(DateTime.Today - BirthDate.Value).TotalDays : 0;

    // 仅用于 UI 展示
    public string GenderEmoji => Gender == "girl" ? "👧" : "👦";
    public string GenderText => Gender == "girl" ? "女宝" : "男宝";
    public string BirthDateText => BirthDate.HasValue
        ? ServiceProvider.Instance.DateTimeFormatter.FormatDate(BirthDate.Value)
        : "未设置生日";
}
