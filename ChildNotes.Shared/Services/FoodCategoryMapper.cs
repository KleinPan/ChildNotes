using System.Collections.Concurrent;

namespace ChildNotes.Shared.Services;

/// <summary>
/// 辅食食材分类映射器：根据食材名称推断分类标签（主食/蔬菜/水果/肉蛋/其他）。
/// 用于规则解析器自动填充 FoodTypes，以及展示层将具体食材名转为简短分类标签。
/// 分类体系对齐 ComplementaryFormViewModel 的 4 大默认类别 + "其他"兜底。
/// </summary>
public static class FoodCategoryMapper
{
    /// <summary>分类标签常量</summary>
    public const string CategoryStaple = "主食";
    public const string CategoryVegetable = "蔬菜";
    public const string CategoryFruit = "水果";
    public const string CategoryMeatEgg = "肉蛋";
    public const string CategoryOther = "其他";

    /// <summary>
    /// 食材名 → 分类标签映射表（线程安全只读字典）。
    /// 键为小写食材名（去除"泥"/"粥"等后缀以便模糊匹配），值为分类标签。
    /// </summary>
    private static readonly ConcurrentDictionary<string, string> Map = new(CreateMap());

    private static Dictionary<string, string> CreateMap()
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // ===== 主食 =====
            { "米粉", CategoryStaple },
            { "面条", CategoryStaple },
            { "小米粥", CategoryStaple },
            { "粥", CategoryStaple },

            // ===== 蔬菜 =====
            { "南瓜泥", CategoryVegetable },
            { "南瓜", CategoryVegetable },
            { "土豆泥", CategoryVegetable },
            { "土豆", CategoryVegetable },
            { "胡萝卜泥", CategoryVegetable },
            { "胡萝卜", CategoryVegetable },
            { "菠菜", CategoryVegetable },
            { "西兰花", CategoryVegetable },
            { "红薯", CategoryVegetable },

            // ===== 水果 =====
            { "苹果泥", CategoryFruit },
            { "苹果", CategoryFruit },
            { "香蕉泥", CategoryFruit },
            { "香蕉", CategoryFruit },
            { "牛油果", CategoryFruit },
            { "梨", CategoryFruit },
            { "果泥", CategoryFruit },

            // ===== 肉蛋 =====
            { "蛋黄", CategoryMeatEgg },
            { "蛋白", CategoryMeatEgg },
            { "鸡肉泥", CategoryMeatEgg },
            { "鱼肉泥", CategoryMeatEgg },
            { "肝粉", CategoryMeatEgg },
            { "牛肉泥", CategoryMeatEgg },
            { "猪肉", CategoryMeatEgg },
            { "虾", CategoryMeatEgg },

            // ===== 其他（调味品/油脂等）=====
            { "核桃油", CategoryOther },
            { "油", CategoryOther },
        };
        return dict;
    }

    /// <summary>
    /// 根据单个食材名获取分类标签。未匹配时返回 null。
    /// </summary>
    public static string? GetCategory(string? foodName)
    {
        if (string.IsNullOrWhiteSpace(foodName)) return null;
        return Map.TryGetValue(foodName.Trim(), out var cat) ? cat : null;
    }

    /// <summary>
    /// 从 FoodName（顿号分隔的多食材字符串）中提取去重的分类标签列表。
    /// 例如 "米粉、肝粉、肉泥、核桃油、香蕉" → ["主食","肉蛋","其他","水果"]
    /// </summary>
    public static List<string> ExtractCategories(string? foodName)
    {
        if (string.IsNullOrWhiteSpace(foodName)) return new();
        var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in foodName.Split('、', ',', '，'))
        {
            var trimmed = item.Trim();
            if (!string.IsNullOrEmpty(trimmed) && Map.TryGetValue(trimmed, out var cat))
                categories.Add(cat);
        }
        return categories.ToList();
    }
}
