using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace ChildNotes.Models;

/// <summary>
/// 应用内消息实体：轻量推送替代方案。
/// 存储后端推送下发的消息，用户打开 App 时拉取展示。
/// 实现 INotifyPropertyChanged 以便 UI 绑定 IsRead 后能即时刷新红点。
/// </summary>
public class InAppMessage : INotifyPropertyChanged
{
    public string Id { get; set; } = string.Empty;

    /// <summary>消息归属用户 ID。</summary>
    public string UserId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    /// <summary>消息分类：general/family_joined/ai_report/points/operation 等。</summary>
    public string Category { get; set; } = "general";

    /// <summary>附加数据（JSON），用于点击跳转路由。</summary>
    public string DataJson { get; set; } = "{}";

    private bool _isRead;

    /// <summary>是否已读。setter 触发 PropertyChanged，使 UI 红点立即响应。</summary>
    public bool IsRead
    {
        get => _isRead;
        set
        {
            if (_isRead != value)
            {
                _isRead = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>消息创建时间（UTC ISO 8601）。</summary>
    public string CreatedAt { get; set; } = string.Empty;

    /// <summary>已读时间（UTC ISO 8601，未读为 null）。</summary>
    public string? ReadAt { get; set; }

    /// <summary>展示用的时间文本（由 ViewModel 转换）。</summary>
    [JsonIgnore]
    public string DisplayTime => FormatDisplayTime(CreatedAt);

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>将 ISO 8601 时间转为"刚刚/X 分钟前/X 小时前/X 天前/日期"。</summary>
    private static string FormatDisplayTime(string isoTime)
    {
        if (string.IsNullOrEmpty(isoTime)) return string.Empty;
        try
        {
            var dt = DateTime.Parse(isoTime, null, System.Globalization.DateTimeStyles.RoundtripKind);
            var local = dt.Kind == DateTimeKind.Utc ? dt.ToLocalTime() : dt;
            var diff = DateTime.Now - local;
            if (diff.TotalMinutes < 1) return "刚刚";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} 分钟前";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours} 小时前";
            if (diff.TotalDays < 7) return $"{(int)diff.TotalDays} 天前";
            return local.ToString("yyyy-MM-dd");
        }
        catch { return isoTime; }
    }
}
