namespace ChildNotes.Core.Dtos;

/// <summary>
/// HA 集成单条喂养记录。时间统一 ISO 8601 带 +08:00 偏移，供 HA device_class: timestamp 直接解析。
/// </summary>
public class HaFeedItem
{
    public string Id { get; set; } = string.Empty;

    /// <summary>喂养时间（ISO 8601，+08:00）。</summary>
    public string Time { get; set; } = string.Empty;

    /// <summary>feed 子类型：breast 亲喂 / bottle 瓶喂 / expressed 吸出母乳。</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>亲喂侧：left / right / both，亲喂以外为 null。</summary>
    public string? Side { get; set; }

    /// <summary>喂养时长（分钟，四舍五入）。无时长场景为 null。</summary>
    public int? DurationMin { get; set; }

    /// <summary>奶量（ml）。亲喂为 null。</summary>
    public int? AmountMl { get; set; }

    /// <summary>备注。</summary>
    public string? Note { get; set; }
}

/// <summary>当日喂养汇总（由后端算好，客户端直接展示）。</summary>
public class HaFeedSummary
{
    public int Count { get; set; }
    public int TotalMl { get; set; }
    public int BreastCount { get; set; }
    public int BottleCount { get; set; }
    public int ExpressedCount { get; set; }

    /// <summary>当日亲喂+瓶喂总时长（分钟，向下取整）。</summary>
    public int TotalDurationMin { get; set; }

    /// <summary>最近一次喂养时间（ISO 8601，+08:00）。当日无记录为 null。</summary>
    public string? LastFeedTime { get; set; }

    /// <summary>最近一次喂养子类型。当日无记录为 null。</summary>
    public string? LastFeedType { get; set; }
}

/// <summary>GET /api/ha/feeding 响应 data 体。</summary>
public class HaFeedingDay
{
    /// <summary>业务日（北京自然日）yyyy-MM-dd。</summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>默认宝宝名。无宝宝为 null。</summary>
    public string? BabyName { get; set; }

    public List<HaFeedItem> Records { get; set; } = new();

    public HaFeedSummary Summary { get; set; } = new();

    /// <summary>服务器当前时间（ISO 8601，+08:00），供客户端校时/判断业务日切换。</summary>
    public string ServerTime { get; set; } = string.Empty;
}
