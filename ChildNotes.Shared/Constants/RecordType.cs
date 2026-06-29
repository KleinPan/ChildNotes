namespace ChildNotes.Shared.Constants;

/// <summary>
/// 记录类型常量。前后端共享，确保数据库存储与 HTTP 协议中字符串值一致。
/// 修改此文件需同步构建前后端。
/// </summary>
public static class RecordType
{
    public const string Feed = "feed";
    public const string Sleep = "sleep";
    public const string Diaper = "diaper";
    public const string Growth = "growth";
    public const string Temperature = "temperature";
    public const string Vaccine = "vaccine";
    public const string Milestone = "milestone";
    public const string Supplement = "supplement";
    public const string Pump = "pump";
    public const string Complementary = "complementary";
    public const string Abnormal = "abnormal";
    public const string Activity = "activity";
    public const string MaternalFood = "maternal-food";
    public const string FeverResolved = "fever_resolved";
    public const string DiarrheaResolved = "diarrhea_resolved";
    public const string AbnormalResolved = "abnormal_resolved";

    /// <summary>特殊类型：AI 智能记。不直接对应一条记录，而是触发文本解析流程。</summary>
    public const string AiNote = "ai-note";

    /// <summary>所有常规记录类型（不含特殊类型 AiNote）。</summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Feed, Sleep, Diaper, Growth, Temperature, Vaccine, Milestone,
        Supplement, Pump, Complementary, Abnormal, Activity, MaternalFood,
        FeverResolved, DiarrheaResolved, AbnormalResolved,
    };
}

public static class FeedType
{
    public const string Breast = "breast";
    public const string Bottle = "bottle";
    public const string Expressed = "expressed";
}

public static class DiaperType
{
    public const string Wet = "wet";
    public const string Dirty = "dirty";
    public const string Both = "both";
    public const string Dry = "dry";
}
