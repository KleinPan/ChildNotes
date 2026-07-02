using ChildNotes.Infrastructure;

namespace ChildNotes.Services;

/// <summary>
/// 后端服务器地址管理。
/// 地址优先从 sync_config 表读取（用户可在数据同步页配置），为空时回退到 <see cref="DefaultPrimary"/>。
/// 后期可通过隐藏 UI 编辑入口实现"只读展示"。
/// </summary>
public static class ServerEndpoints
{
    /// <summary>默认主服务器地址（sync_config 中未配置时回退使用）。</summary>
    public const string DefaultPrimary = "https://api.childnotes.example.com";

    /// <summary>
    /// 备用服务器地址（可为空；网络/主服务器故障时自动切换）。
    /// 当前未配置真实备用地址，留空以避免 SyncPolicy 重试时切到不可达的占位域名
    /// 浪费 2-4 秒。配置真实备用地址后可恢复此值。
    /// </summary>
    public const string Fallback = "";

    /// <summary>健康检查路径（HEAD 请求，5s 超时）。</summary>
    public const string HealthPath = "/api/health";

    /// <summary>
    /// 当前生效的主服务器地址：优先读 sync_config，为空时回退到 <see cref="DefaultPrimary"/>。
    /// </summary>
    public static string Primary =>
        ServiceProvider.Instance.SyncConfigRepository.Get().ServerUrl is { Length: > 0 } url
            ? url
            : DefaultPrimary;

    /// <summary>获取所有候选地址（过滤空串），重试时按顺序切换。</summary>
    public static IReadOnlyList<string> GetAll()
    {
        var list = new List<string> { Primary };
        if (!string.IsNullOrWhiteSpace(Fallback)) list.Add(Fallback);
        return list;
    }
}
