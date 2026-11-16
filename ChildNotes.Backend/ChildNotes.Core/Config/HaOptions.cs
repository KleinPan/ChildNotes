namespace ChildNotes.Core.Config;

/// <summary>
/// HA（Home Assistant）集成接口配置。
/// 生产配置在 /opt/childnotes/shared/appsettings.Production.json 的 "Ha" 节点。
/// </summary>
public class HaOptions
{
    /// <summary>
    /// API Key → 用户邮箱 映射。外部集成（HA）请求 /api/ha/* 时携带 X-Api-Key 头，
    /// 命中即以对应用户身份查询数据（默认宝宝）。Key 建议使用 openssl rand -hex 24 生成。
    /// </summary>
    public Dictionary<string, string> ApiKeys { get; set; } = new();
}
