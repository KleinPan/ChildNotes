using System.Net.Http;
using System.Text.Json;
using ChildNotes.Data.Repositories;

namespace ChildNotes.Services;

/// <summary>
/// 积分 API 客户端：调用后端 /api/points/dashboard 实时获取积分余额。
/// 用于 server 模式下需要后端权威积分数据的场景（如 AI 喂养分析扣分前判断），
/// 避免前端本地 SQLite 积分与后端 PostgreSQL 积分不一致导致"积分不足"误判。
/// </summary>
public sealed class PointsApiClient : BaseApiClient
{
    private readonly SyncConfigRepository _cfgRepo;

    public PointsApiClient(SyncConfigRepository cfgRepo) => _cfgRepo = cfgRepo;

    /// <summary>
    /// 从后端实时获取当前用户积分余额。
    /// 失败（server 未配置 / token 无效 / 网络异常）返回 null，调用方回退到本地 SQLite。
    /// </summary>
    public async Task<long?> GetPointsAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await SendAsync(_cfgRepo, HttpMethod.Get, "/api/points/dashboard", null, ct);
            if (resp is null) return null;
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("points", out var p))
                return p.GetInt64();
            return null;
        }
        catch
        {
            return null;
        }
    }
}
