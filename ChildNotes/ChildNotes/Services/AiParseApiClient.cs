using System.Net.Http;
using System.Text.Json.Serialization;
using ChildNotes.Data.Repositories;

namespace ChildNotes.Services;

/// <summary>
/// AI 智能记 API 客户端：调用后端 /api/smart-analysis/parse-note 接口。
/// 失败时返回 null，调用方将降级到本地 LlmClient 或本地规则解析。
/// </summary>
public sealed class AiParseApiClient : BaseApiClient
{
    private readonly SyncConfigRepository _cfgRepo;

    public AiParseApiClient(SyncConfigRepository cfgRepo) => _cfgRepo = cfgRepo;

    /// <summary>调用后端解析接口；后端不可达或返回错误时返回 null。</summary>
    public async Task<AiNoteParseResult?> ParseAsync(string text, CancellationToken ct = default)
    {
        var body = Serialize(new { Text = text });
        using var resp = await SendAsync(_cfgRepo, HttpMethod.Post, "/api/smart-analysis/parse-note", body, ct);
        return resp is null ? null : await ReadDataAsync<AiNoteParseResult>(resp, ct);
    }
}

/// <summary>与后端 AiNoteParseResponse 对齐。</summary>
public sealed class AiNoteParseResult
{
    public string RecordType { get; set; } = string.Empty;
    public string? RecordSubType { get; set; }
    public string? Time { get; set; }
    public int? Amount { get; set; }
    public int? Duration { get; set; }
    public int? LeftDuration { get; set; }
    public int? RightDuration { get; set; }
    public decimal? Temperature { get; set; }
    public decimal? Height { get; set; }
    public decimal? Weight { get; set; }
    public string? DiaperType { get; set; }
    public string? Note { get; set; }
    public string? Summary { get; set; }
    public double Confidence { get; set; }
    public bool Saved { get; set; }
    public long? RecordId { get; set; }
}
