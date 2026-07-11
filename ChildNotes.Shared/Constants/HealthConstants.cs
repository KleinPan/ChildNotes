namespace ChildNotes.Shared.Constants;

/// <summary>
/// 健康相关阈值常量。前后端共享，确保判定一致。
/// </summary>
public static class HealthConstants
{
    /// <summary>发烧阈值（℃）：腋温 ≥ 37.3℃ 判定为异常。</summary>
    public const decimal FeverThreshold = 37.3m;
}
