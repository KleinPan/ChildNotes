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

    /// <summary>ListFamiliesAsync 内存缓存：避免短时间内重复拉取同一接口。</summary>
    private List<BabyFamilyItem>? _familiesCache;
    private DateTime _familiesCacheAt;
    private static readonly TimeSpan FamiliesCacheTtl = TimeSpan.FromSeconds(15);

    /// <summary>ListFamiliesAsync 请求去重：并发调用合并为单次 HTTP。</summary>
    private readonly SemaphoreSlim _familiesGate = new(1, 1);
    private Task<List<BabyFamilyItem>?>? _familiesInFlight;

    public FamilyApiClient(SyncConfigRepository cfgRepo) => _cfgRepo = cfgRepo;

    /// <summary>列出当前用户加入的所有家庭及其成员。</summary>
    public async Task<List<BabyFamilyItem>?> ListFamiliesAsync(CancellationToken ct = default)
    {
        // 命中缓存（未过期）直接返回
        var now = DateTime.UtcNow;
        if (_familiesCache is not null && (now - _familiesCacheAt) < FamiliesCacheTtl)
        {
            return _familiesCache;
        }

        // 请求去重：并发调用合并为单次 HTTP
        await _familiesGate.WaitAsync(ct);
        try
        {
            // 二次检查缓存（可能在等待锁期间已被其他调用填充）
            now = DateTime.UtcNow;
            if (_familiesCache is not null && (now - _familiesCacheAt) < FamiliesCacheTtl)
            {
                return _familiesCache;
            }

            // 若已有相同请求在飞，复用其 Task
            if (_familiesInFlight is not null)
            {
                return await _familiesInFlight;
            }

            _familiesInFlight = DoListFamiliesAsync(ct);
            try
            {
                var result = await _familiesInFlight;
                if (result is not null)
                {
                    _familiesCache = result;
                    _familiesCacheAt = DateTime.UtcNow;
                }
                return result;
            }
            finally
            {
                _familiesInFlight = null;
            }
        }
        finally
        {
            _familiesGate.Release();
        }
    }

    private async Task<List<BabyFamilyItem>?> DoListFamiliesAsync(CancellationToken ct)
    {
        using var resp = await SendAsync(_cfgRepo, HttpMethod.Get, "/api/baby/family/members", null, ct);
        return resp is null ? null : await ReadDataAsync<List<BabyFamilyItem>>(resp, ct);
    }

    /// <summary>修改本人在指定宝宝家庭中的角色。</summary>
    public async Task<FamilyMemberItem?> UpdateMyRoleAsync(string babyId, string roleCode, CancellationToken ct = default)
    {
        var body = Serialize(new { babyId, roleCode });
        using var resp = await SendAsync(_cfgRepo, HttpMethod.Put, "/api/baby/family/my-role", body, ct);
        var result = resp is null ? null : await ReadDataAsync<FamilyMemberItem>(resp, ct);
        // 写入操作后失效缓存，下次读取会拉取最新数据
        InvalidateFamiliesCache();
        return result;
    }

    /// <summary>通过宝宝 ID 加入家庭（后端会把当前用户加到该宝宝所属家庭的所有宝宝下）。</summary>
    public async Task<FamilyMemberItem?> JoinFamilyAsync(string babyId, string roleCode, CancellationToken ct = default)
    {
        var body = Serialize(new { babyId, roleCode });
        using var resp = await SendAsync(_cfgRepo, HttpMethod.Post, "/api/baby/family/join", body, ct);
        var result = resp is null ? null : await ReadDataAsync<FamilyMemberItem>(resp, ct);
        InvalidateFamiliesCache();
        return result;
    }

    /// <summary>失效家庭列表缓存。写入操作后调用，确保下次读取拉取最新数据。</summary>
    public void InvalidateFamiliesCache()
    {
        _familiesCache = null;
        _familiesCacheAt = default;
    }
}

// BabyFamilyItem / FamilyMemberItem 已迁移至 ChildNotes.Shared.Dtos（前后端共享）
// 本文件顶部通过 using 别名保留前端历史命名，调用方代码无需改动。

