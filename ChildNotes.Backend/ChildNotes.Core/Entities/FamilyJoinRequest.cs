using ChildNotes.Core.Constants;

namespace ChildNotes.Core.Entities;

/// <summary>
/// 家庭加入申请记录。
/// 状态机：pending（待审） → approved / rejected / cancelled
/// approved 后会创建/复活对应 BabyMember 记录。
/// </summary>
public class FamilyJoinRequest : IAuditable
{
    public string Id { get; set; } = string.Empty;
    /// <summary>目标宝宝 ID（申请人凭此 ID 提交申请）。</summary>
    public string BabyId { get; set; } = string.Empty;
    /// <summary>申请人用户 ID。</summary>
    public string ApplicantUserId { get; set; } = string.Empty;
    public string RoleCode { get; set; } = "other";
    public string RoleName { get; set; } = string.Empty;
    /// <summary>申请状态：pending/approved/rejected/cancelled。</summary>
    public string Status { get; set; } = StatusConstants.FamilyJoinRequest.Pending;
    /// <summary>owner 审批/拒绝时间（pending 时为 null）。</summary>
    public DateTime? ProcessedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
