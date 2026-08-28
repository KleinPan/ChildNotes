using System.Net.Http;
using System.Text.Json;
using ChildNotes.Data.Repositories;
using ChildNotes.Shared.Constants;
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

    /// <summary>
    /// 调用后端解析接口；后端不可达或返回错误时返回 null。
    /// <paramref name="parseMode"/> 见 <see cref="ParseMode"/>；默认 <see cref="ParseMode.Fast"/>。
    /// <paramref name="usePointsForOverage"/> 为 true 时（免费次数用尽后用户选择积分抵扣），
    /// 才把 UsePointsForOverage=true 序列化进请求体；默认 false 不携带该字段。
    /// </summary>
    public async Task<AiNoteParseBatchResponse?> ParseAsync(string text, string parseMode = ParseMode.Fast, bool usePointsForOverage = false, CancellationToken ct = default)
    {
        var body = Serialize(BuildParseBody(text, parseMode, usePointsForOverage));
        using var resp = await SendAsync(_cfgRepo, HttpMethod.Post, "/api/smart-analysis/parse-note", body, ct);
        return resp is null ? null : await ReadDataAsync<AiNoteParseBatchResponse>(resp, ct);
    }

    /// <summary>
    /// 调用后端解析接口，失败时抛出带错误码的异常（而非返回 null）。
    /// 供需要区分"AI 次数用尽"等业务错误的调用方使用。
    /// <paramref name="parseMode"/> 见 <see cref="ParseMode"/>；默认 <see cref="ParseMode.Fast"/>。
    /// <paramref name="usePointsForOverage"/> 为 true 时才序列化进请求体（积分抵扣模式）。
    /// </summary>
    public async Task<AiNoteParseBatchResponse> ParseWithErrorsAsync(string text, string parseMode = ParseMode.Fast, bool usePointsForOverage = false, CancellationToken ct = default)
    {
        var body = Serialize(BuildParseBody(text, parseMode, usePointsForOverage));
        // 用 SendWithErrorAsync 而非 SendAsync：后者会把所有非 2xx 响应吞成 null，
        // 导致后端业务错误（AI 次数用尽等）的 msg/code 丢失，最终统一抛"后端服务不可用"。
        using var resp = await SendWithErrorAsync(_cfgRepo, HttpMethod.Post, "/api/smart-analysis/parse-note", body, ct);
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

    /// <summary>
    /// 构造 /parse-note 请求体。
    /// <paramref name="usePointsForOverage"/> 为 true 时（免费次数用尽后用户选择积分抵扣），
    /// 才把 UsePointsForOverage=true 序列化进请求体；默认 false 不携带该字段。
    /// </summary>
    private static object BuildParseBody(string text, string parseMode, bool usePointsForOverage)
        => usePointsForOverage
            ? new { Text = text, ParseMode = parseMode, UsePointsForOverage = true }
            : new { Text = text, ParseMode = parseMode };
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

    /// <summary>是否为积分不足错误（免费次数用尽后选择积分抵扣，但积分余额不够）。</summary>
    public bool IsInsufficientPoints => ErrorCode == "INSUFFICIENT_POINTS";

    public AiNoteApiException(string message, string? errorCode) : base(message)
    {
        ErrorCode = errorCode;
    }
}
