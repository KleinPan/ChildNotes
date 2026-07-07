using System.Net.Http;
using ChildNotes.Infrastructure;

namespace ChildNotes.Services;

/// <summary>
/// 同步/API 调用的轻量重试策略。手写实现，不引入 Polly。
/// 针对 <see cref="SyncErrorKind"/> 分别配置重试次数与指数退避，
/// 并在重试时通过 <see cref="ServerEndpoints"/> 切换备用服务器地址。
/// </summary>
public static class SyncPolicy
{
    /// <summary>各类错误的最大重试次数（首次执行不计入）。</summary>
    private static int MaxRetry(SyncErrorKind kind) => kind switch
    {
        SyncErrorKind.Network => 3,
        SyncErrorKind.Timeout => 3,
        SyncErrorKind.Server5xx => 2,
        SyncErrorKind.Auth => 1, // 仅一次：重新登录后再试
        _ => 0,
    };

    /// <summary>第 attempt 次重试前的等待毫秒（attempt 从 0 起，0 表示首次失败后的第一次重试前）。</summary>
    private static int BackoffMs(int attempt, SyncErrorKind kind) => kind switch
    {
        SyncErrorKind.Network or SyncErrorKind.Timeout => (1 << attempt) * 1000,  // 1s, 2s, 4s
        SyncErrorKind.Server5xx => (1 << attempt) * 2000,                          // 2s, 4s
        _ => 0,
    };

    /// <summary>
    /// 执行一次可重试操作。每次重试会传入当前 attempt（0=首次）与当前服务器地址。
    /// 首次失败后的第一次重试（attempt 从 0 自增为 1）即切换到 <see cref="ServerEndpoints.Fallback"/>（若配置）。
    /// </summary>
    /// <typeparam name="T">返回类型。</typeparam>
    /// <param name="fn">业务函数，签名为 (attempt, serverUrl) => Task{T}。</param>
    /// <param name="initialServer">初始服务器地址。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>成功则返回结果；不可重试或重试用尽则抛 <see cref="SyncException"/>。</returns>
    public static async Task<T> ExecuteAsync<T>(
        Func<int, string, Task<T>> fn,
        string initialServer,
        CancellationToken ct = default)
    {
        var servers = ServerEndpoints.GetAll();
        int initialIdx = -1;
        for (int i = 0; i < servers.Count; i++)
        {
            if (string.Equals(servers[i], initialServer, StringComparison.OrdinalIgnoreCase))
            {
                initialIdx = i;
                break;
            }
        }
        if (initialIdx < 0) initialIdx = 0;

        int attempt = 0;
        string currentServer = initialServer;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return await fn(attempt, currentServer);
            }
            catch (SyncException ex) when (ex.Transient && attempt < MaxRetry(ex.Kind))
            {
                int delay = BackoffMs(attempt, ex.Kind);
                DevLogger.Log("SyncPolicy",
                    $"attempt {attempt} failed ({ex.Kind}), retry in {delay}ms");
                attempt++;
                // 第二次重试起切换备用地址
                if (attempt >= 1 && servers.Count > 1)
                {
                    var next = servers[(initialIdx + attempt) % servers.Count];
                    if (next != currentServer)
                    {
                        currentServer = next;
                        DevLogger.Log("SyncPolicy", $"switch server -> {currentServer}");
                    }
                }
                await Task.Delay(delay, ct);
            }
        }
    }
}
