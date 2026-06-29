using ChildNotes.Infrastructure;

namespace ChildNotes.Models;

public sealed class AiAnalysisRecord
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public long BabyId { get; set; }
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
    /// "Ai记" 功能的解析服务来源：
    /// <list type="bullet">
    ///   <item><c>local</c>（默认）：调用用户在本页配置的 OpenAI 兼容大模型。</item>
    ///   <item><c>server</c>：调用后端 /api/smart-analysis/parse-note 接口（依赖同步服务器配置）。</item>
    /// </list>
    /// </summary>
    public string NoteSource { get; set; } = "local";
}
