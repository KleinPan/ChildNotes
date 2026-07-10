using ChildNotes.Infrastructure;

namespace ChildNotes.Models;

public sealed class AiAnalysisRecord
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string BabyId { get; set; } = string.Empty;
    public string BabyName { get; set; } = string.Empty;
    public DateTime RangeStartDate { get; set; }
    public DateTime RangeEndDate { get; set; }
    public string AnalysisText { get; set; } = string.Empty;
    public string DataQualityTip { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public string RangeLabel => $"{RangeStartDate:yyyy-MM-dd} 至 {RangeEndDate:yyyy-MM-dd}";
    public string CreatedAtLabel => ServiceProvider.Instance.DateTimeFormatter.FormatDateTime(CreatedAt.ToLocalTime());
    public string Preview => AnalysisText.Length > 80 ? AnalysisText[..80] + "..." : AnalysisText;
}

public sealed class LlmConfig
{
    public string ApiBaseUrl { get; set; } = "https://api.openai.com";
    public string ApiKey { get; set; } = string.Empty;
    public string ModelName { get; set; } = "gpt-4o-mini";
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 2048;
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// AI 功能的解析服务来源，同时作用于"Ai记"和"宝宝喂养分析"：
    /// <list type="bullet">
    ///   <item><c>local</c>（默认）：调用用户在本页配置的 OpenAI 兼容大模型。</item>
    ///   <item><c>server</c>：调用后端 /api/smart-analysis/* 接口（parse-note / generate，依赖同步服务器配置）。</item>
    /// </list>
    /// </summary>
    public string NoteSource { get; set; } = "local";
}

/// <summary>
/// 后端 /api/smart-analysis/generate 和 /list 返回的分析记录 DTO。
/// 字段与后端 AiAnalysisRecordDto 对应（camelCase 反序列化由 BaseApiClient.JsonOpts 处理）。
/// </summary>
public sealed class ServerAiAnalysisDto
{
    public string Id { get; set; } = string.Empty;
    public string BabyId { get; set; } = string.Empty;
    public string BabyName { get; set; } = string.Empty;
    public string RangeStartDate { get; set; } = string.Empty;
    public string RangeEndDate { get; set; } = string.Empty;
    public string AnalysisText { get; set; } = string.Empty;
    public string DataQualityTip { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
}
