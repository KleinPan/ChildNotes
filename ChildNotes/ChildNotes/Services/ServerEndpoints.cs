namespace ChildNotes.Services;

/// <summary>
/// 硬编码的后端服务器地址。作为应用唯一权威来源，用户无需也无法在 UI 配置。
/// 生产环境通过编译常量或此处常量切换；如需多环境，可改为从 Properties 读取。
/// </summary>
public static class ServerEndpoints
{
    /// <summary>主服务器地址（必须配置）。</summary>
    public const string Primary = "https://api.childnotes.example.com";

    /// <summary>
    /// 备用服务器地址（可为空；网络/主服务器故障时自动切换）。
    /// 当前未配置真实备用地址，留空以避免 SyncPolicy 重试时切到不可达的占位域名
    /// 浪费 2-4 秒。配置真实备用地址后可恢复此值。
    /// </summary>
    public const string Fallback = "";

    /// <summary>健康检查路径（HEAD 请求，5s 超时）。</summary>
    public const string HealthPath = "/api/health";

    /// <summary>获取所有候选地址（过滤空串），重试时按顺序切换。</summary>
    public static IReadOnlyList<string> GetAll()
    {
        var list = new List<string> { Primary };
        if (!string.IsNullOrWhiteSpace(Fallback)) list.Add(Fallback);
        return list;
    }
}
