using System.Net.Http;
using ChildNotes.Data.Repositories;
using ChildNotes.Shared.Dtos;

namespace ChildNotes.Services;

/// <summary>
/// AI 智能记 API 客户端：调用后端 /api/smart-analysis/parse-note 接口。
/// 后端已升级为多条解析协议，返回 <see cref="AiNoteParseBatchResponse"/>。
/// 失败时返回 null，调用方将降级到本地 LlmClient 或本地规则解析。
/// </summary>
public sealed class AiParseApiClient : BaseApiClient
{
    private readonly SyncConfigRepository _cfgRepo;

    public AiParseApiClient(SyncConfigRepository cfgRepo) => _cfgRepo = cfgRepo;

    /// <summary>调用后端解析接口；后端不可达或返回错误时返回 null。</summary>
    public async Task<AiNoteParseBatchResponse?> ParseAsync(string text, CancellationToken ct = default)
    {
        var body = Serialize(new { Text = text });
        using var resp = await SendAsync(_cfgRepo, HttpMethod.Post, "/api/smart-analysis/parse-note", body, ct);
        return resp is null ? null : await ReadDataAsync<AiNoteParseBatchResponse>(resp, ct);
    }
}
