using System.Net.Http;
using System.Text.Json;
using ChildNotes.Data.Repositories;
using ChildNotes.Models;

namespace ChildNotes.Services;

/// <summary>
/// AI 分析 API 客户端：调用后端 /api/smart-analysis/generate、/list、/cost 接口。
/// 用于"宝宝喂养分析"功能在 server 模式下走后端生成。
/// 后端 /generate 已具备幂等（同区间+同 SourceText 直接返回缓存）。
/// 积分不足时抛 <see cref="AiAnalysisApiException"/>（ErrorCode=INSUFFICIENT_POINTS）。
/// 其他失败返回 null，由调用方决定如何提示。
/// </summary>
public sealed class AiAnalysisApiClient : BaseApiClient
{
    private readonly SyncConfigRepository _cfgRepo;

    public AiAnalysisApiClient(SyncConfigRepository cfgRepo) => _cfgRepo = cfgRepo;

    /// <summary>
    /// 调用后端生成 7 天喂养分析。
    /// babyId 通过查询参数传递（后端 ResolveBabyIdFromRequest 支持 header/query 两种方式）。
    /// 成功返回 DTO；积分不足抛 <see cref="AiAnalysisApiException"/>；其他失败返回 null。
    /// </summary>
    public async Task<ServerAiAnalysisDto?> GenerateAsync(DateTime start, DateTime end, string? babyId, CancellationToken ct = default)
    {
        var path = $"/api/smart-analysis/generate?babyId={Uri.EscapeDataString(babyId ?? "")}";
        var body = Serialize(new { StartDate = start.ToString("yyyy-MM-dd"), EndDate = end.ToString("yyyy-MM-dd") });
        using var resp = await SendAsync(_cfgRepo, HttpMethod.Post, path, body, ct);
        return resp is null ? null : await ReadDataAsync<ServerAiAnalysisDto>(resp, ct);
    }

    /// <summary>
    /// 调用后端生成分析，失败时抛出带错误码的异常（而非返回 null）。
    /// 供需要区分"积分不足"等业务错误的调用方使用。
    /// </summary>
    public async Task<ServerAiAnalysisDto> GenerateWithErrorsAsync(DateTime start, DateTime end, string? babyId, CancellationToken ct = default)
    {
        var path = $"/api/smart-analysis/generate?babyId={Uri.EscapeDataString(babyId ?? "")}";
        var body = Serialize(new { StartDate = start.ToString("yyyy-MM-dd"), EndDate = end.ToString("yyyy-MM-dd") });
        using var resp = await SendAsync(_cfgRepo, HttpMethod.Post, path, body, ct);
        if (resp is null)
            throw new AiAnalysisApiException("后端服务不可用，请检查同步服务器配置或网络连接", null);
        if (!resp.IsSuccessStatusCode)
        {
            var (msg, code) = await ReadErrorAsync(resp, ct);
            throw new AiAnalysisApiException(msg, code);
        }
        var dto = await ReadDataAsync<ServerAiAnalysisDto>(resp, ct);
        return dto ?? throw new AiAnalysisApiException("后端返回数据格式异常", null);
    }

    /// <summary>调用后端列出当前宝宝的分析记录。</summary>
    public async Task<List<ServerAiAnalysisDto>?> ListAsync(string? babyId, CancellationToken ct = default)
    {
        var path = $"/api/smart-analysis/list?babyId={Uri.EscapeDataString(babyId ?? "")}";
        using var resp = await SendAsync(_cfgRepo, HttpMethod.Get, path, null, ct);
        return resp is null ? null : await ReadDataAsync<List<ServerAiAnalysisDto>>(resp, ct);
    }

    /// <summary>
    /// 查询当前 AI 喂养分析所需消耗的积分数量。
    /// 失败返回默认值 10（<see cref="Models.PointsConstants.AiAnalysisDefaultCost"/>）。
    /// </summary>
    public async Task<int> GetCostAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await SendAsync(_cfgRepo, HttpMethod.Get, "/api/smart-analysis/cost", null, ct);
            if (resp is null) return PointsConstants.AiAnalysisDefaultCost;
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("costPoints", out var cp))
                return cp.GetInt32();
            return PointsConstants.AiAnalysisDefaultCost;
        }
        catch
        {
            return PointsConstants.AiAnalysisDefaultCost;
        }
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
/// AI 分析 API 业务异常：携带后端返回的错误码（如 INSUFFICIENT_POINTS）。
/// 供 ViewModel 区分"积分不足"等可操作错误与其他网络错误。
/// </summary>
public sealed class AiAnalysisApiException : Exception
{
    /// <summary>后端返回的业务错误码（如 INSUFFICIENT_POINTS），可能为 null。</summary>
    public string? ErrorCode { get; }

    /// <summary>是否为积分不足错误。</summary>
    public bool IsInsufficientPoints => ErrorCode == "INSUFFICIENT_POINTS";

    public AiAnalysisApiException(string message, string? errorCode) : base(message)
    {
        ErrorCode = errorCode;
    }
}
