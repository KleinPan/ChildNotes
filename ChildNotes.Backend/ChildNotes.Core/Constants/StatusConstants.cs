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
}
