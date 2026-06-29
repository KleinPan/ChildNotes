using System.Net.Http;
using ChildNotes.Data.Repositories;
using ChildNotes.Shared.Constants;
using ChildNotes.Shared.Dtos;
// 前端历史命名 → 共享 DTO 别名（保持调用方代码不变）
using BabyFamilyItem = ChildNotes.Shared.Dtos.BabyFamilyDto;
using FamilyMemberItem = ChildNotes.Shared.Dtos.BabyMemberDto;

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

// BabyFamilyItem / FamilyMemberItem 已迁移至 ChildNotes.Shared.Dtos（前后端共享）
// 本文件顶部通过 using 别名保留前端历史命名，调用方代码无需改动。

