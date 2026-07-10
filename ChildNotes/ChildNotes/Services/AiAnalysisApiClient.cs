using System.Net.Http;
using ChildNotes.Data.Repositories;
using ChildNotes.Models;

namespace ChildNotes.Services;

/// <summary>
/// AI 分析 API 客户端：调用后端 /api/smart-analysis/generate 和 /list 接口。
/// 用于"宝宝喂养分析"功能在 server 模式下走后端生成。
/// 后端 /generate 已具备幂等（同区间+同 SourceText 直接返回缓存）。
/// 失败时返回 null，由调用方决定如何提示。
/// </summary>
public sealed class AiAnalysisApiClient : BaseApiClient
{
    private readonly SyncConfigRepository _cfgRepo;

    public AiAnalysisApiClient(SyncConfigRepository cfgRepo) => _cfgRepo = cfgRepo;

    /// <summary>
    /// 调用后端生成 7 天喂养分析。
    /// babyId 通过查询参数传递（后端 ResolveBabyIdFromRequest 支持 header/query 两种方式）。
    /// 成功返回 DTO，失败（未配置/鉴权失败/HTTP 错误）返回 null。
    /// </summary>
    public async Task<ServerAiAnalysisDto?> GenerateAsync(DateTime start, DateTime end, string? babyId, CancellationToken ct = default)
    {
        var path = $"/api/smart-analysis/generate?babyId={Uri.EscapeDataString(babyId ?? "")}";
        var body = Serialize(new { StartDate = start.ToString("yyyy-MM-dd"), EndDate = end.ToString("yyyy-MM-dd") });
        using var resp = await SendAsync(_cfgRepo, HttpMethod.Post, path, body, ct);
        return resp is null ? null : await ReadDataAsync<ServerAiAnalysisDto>(resp, ct);
    }

    /// <summary>调用后端列出当前宝宝的分析记录。</summary>
    public async Task<List<ServerAiAnalysisDto>?> ListAsync(string? babyId, CancellationToken ct = default)
    {
        var path = $"/api/smart-analysis/list?babyId={Uri.EscapeDataString(babyId ?? "")}";
        using var resp = await SendAsync(_cfgRepo, HttpMethod.Get, path, null, ct);
        return resp is null ? null : await ReadDataAsync<List<ServerAiAnalysisDto>>(resp, ct);
    }
}
