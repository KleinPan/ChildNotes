namespace ChildNotes.Shared.Constants;

/// <summary>
/// 家庭角色选项。前后端共享，确保 UI 选项与数据库存储的 role code 一致。
/// </summary>
public static class FamilyRoles
{
    public static readonly IReadOnlyList<RoleOption> All = new[]
    {
        new RoleOption("father", "爸爸"),
        new RoleOption("mother", "妈妈"),
        new RoleOption("grandpa", "爷爷"),
        new RoleOption("grandma", "奶奶"),
        new RoleOption("maternalGrandpa", "外公"),
        new RoleOption("maternalGrandma", "外婆"),
        new RoleOption("uncle", "叔叔"),
        new RoleOption("aunt", "阿姨"),
        new RoleOption("paternalAunt", "姑姑"),
        new RoleOption("maternalUncle", "舅舅"),
        new RoleOption("nanny", "保姆"),
        new RoleOption("other", "其他"),
    };

    public static string GetRoleName(string code)
    {
        foreach (var r in All)
        {
            if (r.Code == code) return r.Name;
        }
        return "家人";
    }
}

public sealed class RoleOption
{
    public string Code { get; }
    public string Name { get; }
    public RoleOption(string code, string name) { Code = code; Name = name; }
}
