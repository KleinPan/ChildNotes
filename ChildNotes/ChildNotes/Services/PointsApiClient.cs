using System.Net.Http;
using System.Text.Json;
using ChildNotes.Data.Repositories;

namespace ChildNotes.Services;

/// <summary>
/// 积分 API 客户端：调用后端 /api/points/* 实时获取积分数据。
/// 用于 server 模式下需要后端权威积分数据的场景（如 AI 喂养分析扣分前判断、任务赚积分领取），
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

    /// <summary>
    /// 从后端获取任务列表（含完成/领取状态）。失败返回 null，调用方回退到本地任务展示（不可领取）。
    /// </summary>
    public async Task<List<ServerTaskItem>?> GetTasksAsync(CancellationToken ct = default)
    {
        using var resp = await SendAsync(_cfgRepo, HttpMethod.Get, "/api/points/tasks", null, ct);
        return resp is null ? null : await ReadDataAsync<List<ServerTaskItem>>(resp, ct);
    }

    /// <summary>
    /// 调后端签到 API（POST /api/points/sign-in），返回签到后的最新积分余额。
    /// 后端签到是幂等的（今日已签到返回 ALREADY_SIGNED_IN 错误码）。
    /// 失败（网络错误等）返回 null，调用方回退到本地签到。
    /// </summary>
    public async Task<SignInResult?> SignInAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await SendWithErrorAsync(_cfgRepo, HttpMethod.Post, "/api/points/sign-in", null, ct);
            if (resp is null) return null;
            if (!resp.IsSuccessStatusCode)
            {
                var (msg, code) = await ReadErrorAsync(resp, ct);
                // ALREADY_SIGNED_IN 不算错误，返回 null 让调用方走本地回退（本地已签到会被跳过）
                if (code == "ALREADY_SIGNED_IN") return new SignInResult { AlreadySignedIn = true };
                throw new PointsApiException(msg, code);
            }
            var dto = await ReadDataAsync<SignInResult>(resp, ct);
            return dto;
        }
        catch (PointsApiException) { throw; }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 领取日常任务奖励。成功返回领取结果；失败（任务未完成/已领取/网络错误）抛 <see cref="PointsApiException"/>。
    /// </summary>
    public async Task<ClaimTaskResult> ClaimTaskAsync(string taskKey, CancellationToken ct = default)
    {
        var path = $"/api/points/tasks/{Uri.EscapeDataString(taskKey)}/claim";
        // 用 SendWithErrorAsync 而非 SendAsync：后者会把所有非 2xx 响应吞成 null，
        // 导致后端业务错误（任务未完成/已领取等）的 msg/code 丢失。
        using var resp = await SendWithErrorAsync(_cfgRepo, HttpMethod.Post, path, null, ct);
        if (resp is null)
            throw new PointsApiException("后端服务不可用，请检查同步服务器配置或网络连接", null);
        if (!resp.IsSuccessStatusCode)
        {
            var (msg, code) = await ReadErrorAsync(resp, ct);
            throw new PointsApiException(msg, code);
        }
        var dto = await ReadDataAsync<ClaimTaskResult>(resp, ct);
        return dto ?? throw new PointsApiException("后端返回数据格式异常", null);
    }
}

/// <summary>后端返回的任务项（对应 TaskTemplateDto）。</summary>
public sealed class ServerTaskItem
{
    public string TaskKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Points { get; set; }
    /// <summary>"share"（邀请，跳转分享）或 "claim"（日常任务，可领取）。</summary>
    public string Action { get; set; } = "share";
    public bool IsCompleted { get; set; }
    public bool IsClaimed { get; set; }
}

/// <summary>领取任务奖励响应（对应 ClaimTaskResponse）。</summary>
public sealed class ClaimTaskResult
{
    public int AwardedPoints { get; set; }
    public long Points { get; set; }
    public long TotalEarned { get; set; }
}

/// <summary>后端签到结果（对应 PointsDashboardResponse 的 data 字段）。</summary>
public sealed class SignInResult
{
    public long Points { get; set; }
    public long TotalEarned { get; set; }
    public long TotalSpent { get; set; }
    /// <summary>今日已签到（后端返回 ALREADY_SIGNED_IN 时为 true）。</summary>
    public bool AlreadySignedIn { get; set; }
}

/// <summary>积分 API 业务异常：携带后端返回的错误码。</summary>
public sealed class PointsApiException : Exception
{
    public string? ErrorCode { get; }
    public bool IsTaskNotCompleted => ErrorCode == "TASK_NOT_COMPLETED";
    public bool IsTaskAlreadyClaimed => ErrorCode == "TASK_ALREADY_CLAIMED";

    public PointsApiException(string message, string? errorCode) : base(message)
    {
        ErrorCode = errorCode;
    }
}
