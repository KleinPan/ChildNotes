using System.Net.Http;
using ChildNotes.Data.Repositories;

namespace ChildNotes.Services;

/// <summary>
/// 家人管理 API 客户端：调用后端 /api/baby/family/* 接口。
/// 注意：家人管理为在线功能，需要后端服务可用。
/// </summary>
public sealed class FamilyApiClient : BaseApiClient
{
    private readonly SyncConfigRepository _cfgRepo;

    public FamilyApiClient(SyncConfigRepository cfgRepo) => _cfgRepo = cfgRepo;

    /// <summary>列出当前用户加入的所有家庭及其成员。</summary>
    public async Task<List<BabyFamilyItem>?> ListFamiliesAsync(CancellationToken ct = default)
    {
        using var resp = await SendAsync(_cfgRepo, HttpMethod.Get, "/api/baby/family/members", null, ct);
        return resp is null ? null : await ReadDataAsync<List<BabyFamilyItem>>(resp, ct);
    }

    /// <summary>修改本人在指定宝宝家庭中的角色。</summary>
    public async Task<FamilyMemberItem?> UpdateMyRoleAsync(long babyId, string roleCode, CancellationToken ct = default)
    {
        var body = Serialize(new { babyId, roleCode });
        using var resp = await SendAsync(_cfgRepo, HttpMethod.Put, "/api/baby/family/my-role", body, ct);
        return resp is null ? null : await ReadDataAsync<FamilyMemberItem>(resp, ct);
    }

    /// <summary>通过宝宝 ID 加入家庭（后端会把当前用户加到宝宝主人名下所有宝宝）。</summary>
    public async Task<FamilyMemberItem?> JoinFamilyAsync(long babyId, string roleCode, CancellationToken ct = default)
    {
        var body = Serialize(new { babyId, roleCode });
        using var resp = await SendAsync(_cfgRepo, HttpMethod.Post, "/api/baby/family/join", body, ct);
        return resp is null ? null : await ReadDataAsync<FamilyMemberItem>(resp, ct);
    }
}

/// <summary>与后端 BabyFamilyDto 对齐。</summary>
public sealed class BabyFamilyItem
{
    public long BabyId { get; set; }
    public string BabyName { get; set; } = "";
    public List<FamilyMemberItem> Members { get; set; } = new();
}

/// <summary>与后端 BabyMemberDto 对齐。</summary>
public sealed class FamilyMemberItem
{
    public long Id { get; set; }
    public long BabyId { get; set; }
    public long UserId { get; set; }
    public string NickName { get; set; } = "";
    public string AvatarUrl { get; set; } = "";
    public string RoleCode { get; set; } = "";
    public string RoleName { get; set; } = "";
    public bool Owner { get; set; }
    public bool Mine { get; set; }
}

/// <summary>角色选项（与后端 FamilyRoles 保持一致）。</summary>
public static class FamilyRoleOptions
{
    public static readonly IReadOnlyList<RoleOptionItem> All = new[]
    {
        new RoleOptionItem("father", "爸爸"),
        new RoleOptionItem("mother", "妈妈"),
        new RoleOptionItem("grandpa", "爷爷"),
        new RoleOptionItem("grandma", "奶奶"),
        new RoleOptionItem("maternalGrandpa", "外公"),
        new RoleOptionItem("maternalGrandma", "外婆"),
        new RoleOptionItem("uncle", "叔叔"),
        new RoleOptionItem("aunt", "阿姨"),
        new RoleOptionItem("paternalAunt", "姑姑"),
        new RoleOptionItem("maternalUncle", "舅舅"),
        new RoleOptionItem("nanny", "保姆"),
        new RoleOptionItem("other", "其他"),
    };

    public static string GetRoleName(string code)
    {
        foreach (var r in All) if (r.Code == code) return r.Name;
        return "家人";
    }
}

public sealed class RoleOptionItem
{
    public string Code { get; }
    public string Name { get; }
    public RoleOptionItem(string code, string name) { Code = code; Name = name; }
}
