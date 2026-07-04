namespace ChildNotes.Models.Home;

public sealed class VaccineItem
{
    public string Name { get; }
    /// <summary>分类标签："免费"/"自费"</summary>
    public string Category { get; }
    public int DaysLater { get; }
    public bool IsDone { get; }
    public string DueText => IsDone
        ? "已完成"
        : DaysLater > 0
            ? $"逾期{DaysLater}天"
            : DaysLater == 0
                ? "今天可打"
                : DaysLater == -1
                    ? "待安排"
                    : $"{-DaysLater}天后";
    public VaccineItem(string name, string category, int daysLater, bool isDone)
    {
        Name = name;
        Category = category == "free" ? "免费" : category == "paid" ? "自费" : category;
        DaysLater = daysLater;
        IsDone = isDone;
    }
}
