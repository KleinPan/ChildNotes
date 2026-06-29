namespace ChildNotes.Core.Common;

/// <summary>
/// 分页参数规范化工具，统一各服务中重复的 page/pageSize/skip 计算。
/// </summary>
public static class PagingHelper
{
    /// <summary>规范化分页参数，返回 (page, pageSize, skip)。</summary>
    public static (int page, int pageSize, int skip) Normalize(int page, int pageSize, int defaultSize = 20, int maxSize = 100)
    {
        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? defaultSize : Math.Min(maxSize, pageSize);
        var skip = (page - 1) * pageSize;
        return (page, pageSize, skip);
    }
}
