using System.Net.Http;
using System.Text.Json;
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

    /// <summary>
    /// 调用后端解析接口，失败时抛出带错误码的异常（而非返回 null）。
    /// 供需要区分"AI 次数用尽"等业务错误的调用方使用。
    /// </summary>
    public async Task<AiNoteParseBatchResponse> ParseWithErrorsAsync(string text, CancellationToken ct = default)
    {
        var body = Serialize(new { Text = text });
        using var resp = await SendAsync(_cfgRepo, HttpMethod.Post, "/api/smart-analysis/parse-note", body, ct);
        if (resp is null)
            throw new AiNoteApiException("后端服务不可用，请检查同步服务器配置或网络连接", null);
        if (!resp.IsSuccessStatusCode)
        {
            var (msg, code) = await ReadErrorAsync(resp, ct);
            throw new AiNoteApiException(msg, code);
        }
        var dto = await ReadDataAsync<AiNoteParseBatchResponse>(resp, ct);
        return dto ?? throw new AiNoteApiException("后端返回数据格式异常", null);
    }

    /// <summary>从错误响应中提取 msg 和 code 字段。</summary>
    private static async Task<(string msg, string? code)> ReadErrorAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try
        {
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var msg = doc.RootElement.TryGetProperty("msg", out var m) ? m.GetString() ?? "请求失败" : "请求失败";
            var code = doc.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
            return (msg, code);
        }
        catch
        {
            return ($"请求失败 ({(int)resp.StatusCode})", null);
        }
    }
}

/// <summary>
/// AI 智能记 API 业务异常：携带后端返回的错误码（如 AI_NOTE_LIMIT_EXCEEDED）。
/// 供 ViewModel 区分"AI 次数用尽"等可操作错误与其他网络错误。
/// </summary>
public sealed class AiNoteApiException : Exception
{
    /// <summary>后端返回的业务错误码（如 AI_NOTE_LIMIT_EXCEEDED），可能为 null。</summary>
    public string? ErrorCode { get; }

    /// <summary>是否为 AI 记次数用尽错误（普通用户每日 10 次，会员每日 100 次）。</summary>
    public bool IsAiNoteLimitExceeded => ErrorCode == "AI_NOTE_LIMIT_EXCEEDED";

    public AiNoteApiException(string message, string? errorCode) : base(message)
    {
        ErrorCode = errorCode;
    }
}
