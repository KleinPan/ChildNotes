using System.Collections.ObjectModel;

namespace ChildNotes.Models.Home;

/// <summary>
/// 活动追踪"最近活动"展示项（对齐小程序 activity-tracker 的 lastActivity）。
/// </summary>
public sealed class ActivityLatestItem
{
    public string Name { get; }
    public string Category { get; }
    public int? Duration { get; }
    /// <summary>格式化的记录时间（yyyy-MM-dd HH:mm）。</summary>
    public string Time { get; }
    /// <summary>类别展示文本（🌳 室外 / 🏠 室内），对齐小程序 at-cat。</summary>
    public string CategoryText => Category == "outdoor" ? "🌳 室外" : "🏠 室内";
    /// <summary>类别 emoji（时间轴卡片用），对齐小程序 at-tl-cat。</summary>
    public string CategoryEmoji => Category == "outdoor" ? "🌳" : "🏠";
    /// <summary>时长展示文本（仅当 duration 存在）。</summary>
    public string DurationText => Duration.HasValue ? $"⏱ {Duration}分钟" : string.Empty;

    public ActivityLatestItem(string name, string category, int? duration, string time)
    {
        Name = name; Category = category; Duration = duration; Time = time;
    }
}

/// <summary>活动时间轴分组（按日期分组，对齐小程序 timelineGroups）。</summary>
public sealed class ActivityTimelineGroup
{
    public string Label { get; }
    public bool IsToday { get; }
    public ObservableCollection<ActivityTimelineItem> Items { get; } = new();
    public ActivityTimelineGroup(string label, bool isToday)
    {
        Label = label; IsToday = isToday;
    }
}

/// <summary>活动时间轴单项（对齐小程序 at-tl-item）。</summary>
public sealed class ActivityTimelineItem
{
    public string Name { get; }
    public string Category { get; }
    public int? Duration { get; }
    public string Time { get; }
    public string CategoryEmoji => Category == "outdoor" ? "🌳" : "🏠";
    public string DurationText => Duration.HasValue ? $"⏱ {Duration}分钟" : string.Empty;

    public ActivityTimelineItem(string name, string category, int? duration, string time)
    {
        Name = name; Category = category; Duration = duration; Time = time;
    }
}
