using ChildNotes.Core.Entities;
using ChildNotes.Shared.Entities;

namespace ChildNotes.Core.Entities;

/// <summary>
/// 成长时刻（里程碑）后端实体。继承共享基类 + 实现 IAuditable。
/// 与小程序端 /api/records/milestone 系列接口字段对齐。
/// </summary>
public class Milestone : MilestoneBase, IAuditable
{
    /// <summary>软删标记（同步用）。true 表示已删除，查询时默认过滤。</summary>
    public bool Deleted { get; set; }
}
