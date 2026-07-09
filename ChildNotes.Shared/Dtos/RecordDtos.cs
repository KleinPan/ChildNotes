using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChildNotes.Shared.Dtos;

/// <summary>
/// 记录 DTO。前后端共享，确保 HTTP API 请求/响应字段一致。
/// 修改此文件需同步构建前后端。
/// </summary>
public abstract class BaseRecordDto
{
    public string Id { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
}

public sealed class FeedRecordDto : BaseRecordDto
{
    public string Type { get; set; } = string.Empty;
    public string? Side { get; set; }
    public int? Duration { get; set; }
    public int? LeftDuration { get; set; }
    public int? RightDuration { get; set; }
    public int? LeftDurationSec { get; set; }
    public int? RightDurationSec { get; set; }
    public string? LeftStartTime { get; set; }
    public string? RightStartTime { get; set; }
    public int? Amount { get; set; }
    /// <summary>备注（如"同时喝水 20ml"、"吃后半段哭闹"），存入 PayloadJson，对齐其他类型 Note 字段。</summary>
    public string? Note { get; set; }
}

public sealed class DiaperRecordDto : BaseRecordDto
{
    public string Type { get; set; } = string.Empty;
    public string? Color { get; set; }
    public string? UrineColor { get; set; }
    public string? Consistency { get; set; }
    public List<string> Diarrhea { get; set; } = new();
    public bool Abnormal { get; set; }
    public List<string> Photos { get; set; } = new();
}

public sealed class SleepRecordDto : BaseRecordDto
{
    public string StartTime { get; set; } = string.Empty;
    public string? EndTime { get; set; }
    public int? Duration { get; set; }
}

public sealed class TemperatureRecordDto : BaseRecordDto
{
    public decimal Temperature { get; set; }
    public bool IsAbnormal { get; set; }
    public string? Note { get; set; }
}

public sealed class SupplementRecordDto : BaseRecordDto
{
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>剂量数值文本（如 "0.5"、"1"、"5"），与 DoseUnit 分开存储。</summary>
    public string? Dose { get; set; }
    /// <summary>剂量单位（如 "包"、"粒"、"ml"、"滴"）。旧数据可能为 null（Dose 含单位文本）。</summary>
    public string? DoseUnit { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// 喝水记录 DTO。独立于 supplement，便于统计每日饮水量。
/// AmountMl 为毫升数值；Temperature 可选（水温描述，如"温/凉"）。
/// </summary>
public sealed class WaterRecordDto : BaseRecordDto
{
    public int? AmountMl { get; set; }
    public string? Temperature { get; set; }
    public string? Note { get; set; }
}

public sealed class GrowthRecordDto : BaseRecordDto
{
    public decimal? Height { get; set; }
    public decimal? Weight { get; set; }
}

public sealed class AbnormalRecordDto : BaseRecordDto
{
    public decimal? Temperature { get; set; }
    public List<string> Respiratory { get; set; } = new();
    public List<string> Diarrhea { get; set; } = new();
    public bool Vomit { get; set; }
    public bool Medicine { get; set; }
    public string? Note { get; set; }
    public List<string> Photos { get; set; } = new();
}

public sealed class PumpRecordDto : BaseRecordDto
{
    public int? LeftDuration { get; set; }
    public int? RightDuration { get; set; }
    public int? LeftAmount { get; set; }
    public int? RightAmount { get; set; }
    public int? TotalAmount { get; set; }
    public string? Note { get; set; }
}

public sealed class ComplementaryRecordDto : BaseRecordDto
{
    public List<string> FoodTypes { get; set; } = new();
    public string? Texture { get; set; }
    public string? FoodName { get; set; }
    public string? Amount { get; set; }
    public string? AmountUnit { get; set; }
    public string? Note { get; set; }
    public List<string> Photos { get; set; } = new();
    public string? Reaction { get; set; }
    public bool Abnormal { get; set; }
}

public sealed class VaccineRecordDto : BaseRecordDto
{
    public string Name { get; set; } = string.Empty;
    public string? NextName { get; set; }
    public string? NextDate { get; set; }
    public string? Note { get; set; }
    /// <summary>疫苗目录 Id（如 hepb、bcg），自定义疫苗为 null</summary>
    public string? VaccineId { get; set; }
    /// <summary>剂次 Id（如 dose1），自定义疫苗为 null</summary>
    public string? DoseId { get; set; }
    /// <summary>免费 free / 自费 paid</summary>
    public string? Category { get; set; }
    /// <summary>剂次标签（如 第1剂、1剂）</summary>
    public string? DoseLabel { get; set; }
    /// <summary>状态：done 已打 / skipped 已跳过</summary>
    public string? Status { get; set; }
    /// <summary>是否跳过</summary>
    public bool? Skipped { get; set; }
    /// <summary>跳过原因</summary>
    public string? SkippedReason { get; set; }
    /// <summary>推荐接种日期文本（如 2026-07-01）</summary>
    public string? RecommendedDate { get; set; }
    /// <summary>是否自定义疫苗</summary>
    public bool? Custom { get; set; }
    /// <summary>预防疾病</summary>
    public string? Disease { get; set; }
}

public sealed class ActivityRecordDto : BaseRecordDto
{
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
    public int? Duration { get; set; }
}

public sealed class MilestoneRecordDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Content { get; set; }
    public string Date { get; set; } = string.Empty;
    public List<string> Photos { get; set; } = new();
}

/// <summary>按日期分组的记录响应（后端专用，保留在 Shared 以保持契约完整；前端目前不直接消费此类型）。</summary>
public sealed class DailyRecordsResponse
{
    public DateTime Date { get; set; }
    public Dictionary<string, List<JsonElement>> RecordsByType { get; set; } = new();
}

/// <summary>日统计响应（后端专用）。</summary>
public sealed class DailyStatsResponse
{
    public DateTime Date { get; set; }
    public int TotalCount { get; set; }
    public int? FeedCount { get; set; }
    public int? FeedMl { get; set; }
    public int? SleepCount { get; set; }
    public int? SleepDurationSec { get; set; }
    public int? DiaperCount { get; set; }
    public decimal? MaxTemperature { get; set; }
    public decimal? LatestHeight { get; set; }
    public decimal? LatestWeight { get; set; }
}
