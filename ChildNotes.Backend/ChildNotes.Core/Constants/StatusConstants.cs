namespace ChildNotes.Core.Constants;

/// <summary>
/// Centralized status string constants for entities that use string-typed status columns.
/// Database column values must not change — these constants only replace C# string literals.
/// </summary>
public static class StatusConstants
{
    /// <summary>Admin account status.</summary>
    public static class Admin
    {
        public const string Active = "active";
    }

    /// <summary>Admin lottery activity status (draft → published → closed).</summary>
    public static class AdminLottery
    {
        public const string Draft = "draft";
        public const string Published = "published";
        public const string Closed = "closed";
    }

    /// <summary>Baby member relationship status.</summary>
    public static class BabyMember
    {
        public const string Active = "active";
        /// <summary>被 owner 移除。再次申请加入并经 owner 审批通过后可改回 active。</summary>
        public const string Removed = "removed";
    }

    /// <summary>家庭加入申请状态。</summary>
    public static class FamilyJoinRequest
    {
        public const string Pending = "pending";
        public const string Approved = "approved";
        public const string Rejected = "rejected";
        /// <summary>申请人主动撤回。</summary>
        public const string Cancelled = "cancelled";
    }

    /// <summary>Public lottery activity status.</summary>
    public static class Lottery
    {
        public const string Active = "active";
    }

    /// <summary>Lottery participation record status.</summary>
    public static class LotteryParticipation
    {
        public const string Joined = "joined";
    }

    /// <summary>Task record status.</summary>
    public static class TaskRecord
    {
        public const string Completed = "completed";
    }

    /// <summary>家庭成员角色（FamilyMember.Role）。</summary>
    public static class FamilyMemberRole
    {
        public const string Owner = "owner";
        public const string Member = "member";
        public const string Readonly = "readonly";
    }
}
