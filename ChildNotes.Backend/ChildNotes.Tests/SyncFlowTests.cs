using ChildNotes.Core.Dtos;
using ChildNotes.Shared.Sync;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ChildNotes.Infrastructure.Data;
using System.Net.Http.Json;
using System.Text.Json;

namespace ChildNotes.Tests;

/// <summary>
/// SyncService 集成测试：覆盖 Pull/Push 的核心场景。
/// - 增量游标（since）
/// - LWW 冲突合并（远程较新覆盖，本地较新跳过）
/// - 软删同步（Deleted 字段跨设备传递）
/// - 权限隔离（不同用户不可见彼此数据，不可 push 到他人 baby）
/// - 分页（hasMore / nextCursor）
/// </summary>
public class SyncFlowTests
{
    private static ApiFactory NewFactory() => new();

    private static async Task<HttpClient> NewAuthClientAsync(ApiFactory factory, string username)
    {
        var email = $"{username}@test.local";
        var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/send-code", new SendCodeRequest { Email = email });
        resp.EnsureSuccessStatusCode();
        var code = factory.GetLastCode(email) ?? throw new InvalidOperationException($"未捕获到 {email} 的验证码");
        var verifyResp = await client.PostAsJsonAsync("/api/auth/verify-code",
            new VerifyCodeRequest { Email = email, Code = code });
        verifyResp.EnsureSuccessStatusCode();
        var body = await verifyResp.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("data").GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    private static async Task<string> CreateBabyAsync(HttpClient client, string name = "小宝")
    {
        var resp = await client.PostAsJsonAsync("/api/baby/add", new CreateBabyRequest
        {
            Name = name,
            Gender = "boy",
            BirthDate = new DateTime(2025, 1, 1),
        });
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("data").GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Pull_EmptyReturnsNoData()
    {
        using var factory = NewFactory();
        var client = await NewAuthClientAsync(factory, "sync_empty_" + Guid.NewGuid().ToString("N")[..6]);

        var resp = await client.GetAsync("/api/sync/pull");
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data");
        Assert.Equal(0, data.GetProperty("records").GetArrayLength());
        Assert.Equal(0, data.GetProperty("babies").GetArrayLength());
        Assert.Equal(0, data.GetProperty("milestones").GetArrayLength());
        Assert.False(data.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task Pull_ReturnsOnlyBabiesAccessibleToCurrentUser()
    {
        using var factory = NewFactory();
        var userA = await NewAuthClientAsync(factory, "sync_a_" + Guid.NewGuid().ToString("N")[..6]);
        var userB = await NewAuthClientAsync(factory, "sync_b_" + Guid.NewGuid().ToString("N")[..6]);

        await CreateBabyAsync(userA, "A的宝宝");
        await CreateBabyAsync(userB, "B的宝宝");

        // A pull 只应看到自己的宝宝
        var resp = await userA.GetAsync("/api/sync/pull");
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var babies = body.GetProperty("data").GetProperty("babies");
        Assert.Equal(1, babies.GetArrayLength());
        Assert.Equal("A的宝宝", babies[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Pull_SinceCursorFiltersOldData()
    {
        using var factory = NewFactory();
        var client = await NewAuthClientAsync(factory, "sync_cursor_" + Guid.NewGuid().ToString("N")[..6]);
        await CreateBabyAsync(client, "宝宝1");

        // 第一次 pull 拿到 ServerTime
        var resp1 = await client.GetAsync("/api/sync/pull");
        var body1 = await resp1.Content.ReadFromJsonAsync<JsonElement>();
        var serverTime = body1.GetProperty("data").GetProperty("serverTime").GetDateTime();

        // 用 serverTime 作为 since 再次 pull，不应有数据
        var resp2 = await client.GetAsync($"/api/sync/pull?since={serverTime:O}");
        var body2 = await resp2.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body2.GetProperty("data").GetProperty("babies").GetArrayLength());
    }

    [Fact]
    public async Task Push_CreatesNewRecordsOnServer()
    {
        using var factory = NewFactory();
        var client = await NewAuthClientAsync(factory, "sync_push_" + Guid.NewGuid().ToString("N")[..6]);
        var babyId = await CreateBabyAsync(client);

        // 构造一条记录直接 push（模拟客户端本地生成的数据上行）
        var now = DateTime.UtcNow;
        var req = new SyncBatchRequest
        {
            Records = new()
            {
                new SyncRecordItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    UserId = "", // 占位，服务端会校验
                    BabyId = babyId,
                    RecordType = "feed",
                    RecordDate = DateTime.Today,
                    RecordTime = now,
                    AmountMl = 120,
                    PayloadJson = "{}",
                    CreatedAt = now,
                    UpdatedAt = now,
                }
            }
        };
        // 需要正确的 UserId：从 /api/auth/me 拿
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var uid = me.GetProperty("data").GetProperty("id").GetString()!;
        req.Records[0].UserId = uid;

        var resp = await client.PostAsJsonAsync("/api/sync/push", req);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data");
        Assert.Equal(1, data.GetProperty("recordsUpserted").GetInt32());

        // pull 回来验证
        var pullResp = await client.GetAsync("/api/sync/pull");
        var pullBody = await pullResp.Content.ReadFromJsonAsync<JsonElement>();
        var records = pullBody.GetProperty("data").GetProperty("records");
        Assert.Equal(1, records.GetArrayLength());
        Assert.Equal(120, records[0].GetProperty("amountMl").GetInt32());
    }

    [Fact]
    public async Task Push_LWWConflict_OlderRemoteDoesNotOverwriteNewerLocal()
    {
        using var factory = NewFactory();
        var client = await NewAuthClientAsync(factory, "sync_lww_" + Guid.NewGuid().ToString("N")[..6]);
        var babyId = await CreateBabyAsync(client);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var uid = me.GetProperty("data").GetProperty("id").GetString()!;

        // 第一次 push：写入一条记录，UpdatedAt = T1
        var t1 = DateTime.UtcNow;
        var recId = Guid.NewGuid().ToString("N");
        await client.PostAsJsonAsync("/api/sync/push", new SyncBatchRequest
        {
            Records = new()
            {
                new SyncRecordItem
                {
                    Id = recId, UserId = uid, BabyId = babyId,
                    RecordType = "feed", RecordDate = DateTime.Today, RecordTime = t1,
                    AmountMl = 100, PayloadJson = "{}", CreatedAt = t1, UpdatedAt = t1,
                }
            }
        });

        // 第二次 push：同 Id，AmountMl 改成 200，但 UpdatedAt 更早（T0 < T1）
        var t0 = t1.AddSeconds(-10);
        var resp = await client.PostAsJsonAsync("/api/sync/push", new SyncBatchRequest
        {
            Records = new()
            {
                new SyncRecordItem
                {
                    Id = recId, UserId = uid, BabyId = babyId,
                    RecordType = "feed", RecordDate = DateTime.Today, RecordTime = t1,
                    AmountMl = 200, PayloadJson = "{}", CreatedAt = t0, UpdatedAt = t0,
                }
            }
        });
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        // LWW：远程较旧，不应覆盖
        Assert.Equal(0, body.GetProperty("data").GetProperty("recordsUpserted").GetInt32());

        // pull 验证仍是 100
        var pullResp = await client.GetAsync("/api/sync/pull");
        var pullBody = await pullResp.Content.ReadFromJsonAsync<JsonElement>();
        var rec = pullBody.GetProperty("data").GetProperty("records")[0];
        Assert.Equal(100, rec.GetProperty("amountMl").GetInt32());
    }

    [Fact]
    public async Task Push_LWWConflict_NewerRemoteOverwritesOlderLocal()
    {
        using var factory = NewFactory();
        var client = await NewAuthClientAsync(factory, "sync_lww2_" + Guid.NewGuid().ToString("N")[..6]);
        var babyId = await CreateBabyAsync(client);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var uid = me.GetProperty("data").GetProperty("id").GetString()!;

        var t1 = DateTime.UtcNow;
        var recId = Guid.NewGuid().ToString("N");
        await client.PostAsJsonAsync("/api/sync/push", new SyncBatchRequest
        {
            Records = new()
            {
                new SyncRecordItem
                {
                    Id = recId, UserId = uid, BabyId = babyId,
                    RecordType = "feed", RecordDate = DateTime.Today, RecordTime = t1,
                    AmountMl = 100, PayloadJson = "{}", CreatedAt = t1, UpdatedAt = t1,
                }
            }
        });

        // 第二次 push：同 Id，UpdatedAt 更新（T2 > T1），应覆盖
        var t2 = t1.AddSeconds(10);
        var resp = await client.PostAsJsonAsync("/api/sync/push", new SyncBatchRequest
        {
            Records = new()
            {
                new SyncRecordItem
                {
                    Id = recId, UserId = uid, BabyId = babyId,
                    RecordType = "feed", RecordDate = DateTime.Today, RecordTime = t2,
                    AmountMl = 250, PayloadJson = "{}", CreatedAt = t1, UpdatedAt = t2,
                }
            }
        });
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("data").GetProperty("recordsUpserted").GetInt32());

        var pullResp = await client.GetAsync("/api/sync/pull");
        var pullBody = await pullResp.Content.ReadFromJsonAsync<JsonElement>();
        var rec = pullBody.GetProperty("data").GetProperty("records")[0];
        Assert.Equal(250, rec.GetProperty("amountMl").GetInt32());
    }

    [Fact]
    public async Task Push_RejectsRecordBelongingToOtherUser()
    {
        using var factory = NewFactory();
        var userA = await NewAuthClientAsync(factory, "sync_perm_a_" + Guid.NewGuid().ToString("N")[..6]);
        var userB = await NewAuthClientAsync(factory, "sync_perm_b_" + Guid.NewGuid().ToString("N")[..6]);

        var babyIdA = await CreateBabyAsync(userA, "A的宝宝");
        var meB = await userB.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var uidB = meB.GetProperty("data").GetProperty("id").GetString()!;

        // B 尝试 push 一条记录到 A 的 baby（BabyId 是 A 的，属于另一家庭）
        var now = DateTime.UtcNow;
        var recId = Guid.NewGuid().ToString("N");
        var resp = await userB.PostAsJsonAsync("/api/sync/push", new SyncBatchRequest
        {
            Records = new()
            {
                new SyncRecordItem
                {
                    Id = recId, UserId = uidB, BabyId = babyIdA,
                    RecordType = "feed", RecordDate = DateTime.Today, RecordTime = now,
                    AmountMl = 50, PayloadJson = "{}", CreatedAt = now, UpdatedAt = now,
                }
            }
        });
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data");
        // ForeignFamily terminal skip：babyId 不在 B 当前家庭，记录被跳过并计入 skippedForeignIds
        Assert.Equal(0, data.GetProperty("recordsUpserted").GetInt32());
        var skipped = data.GetProperty("skippedForeignRecordIds");
        Assert.Equal(1, skipped.GetArrayLength());
        Assert.Equal(recId, skipped[0].GetString());
    }

    [Fact]
    public async Task Push_UserIdIsAttributionOnly_ServerStampsCurrentUser()
    {
        using var factory = NewFactory();
        var client = await NewAuthClientAsync(factory, "sync_uid_" + Guid.NewGuid().ToString("N")[..6]);
        var babyId = await CreateBabyAsync(client);

        // Family-centric：家庭业务表的 UserId 仅为创建者归因（本地可能填 LocalDataSpaceId），
        // 授权以 JWT + FamilyMember 为准 —— 不匹配的 UserId 不再拒绝，服务端落库时以 JWT uid 覆盖
        var now = DateTime.UtcNow;
        var resp = await client.PostAsJsonAsync("/api/sync/push", new SyncBatchRequest
        {
            Records = new()
            {
                new SyncRecordItem
                {
                    Id = Guid.NewGuid().ToString("N"), UserId = "local-data-space-id", BabyId = babyId,
                    RecordType = "feed", RecordDate = DateTime.Today, RecordTime = now,
                    AmountMl = 30, PayloadJson = "{}", CreatedAt = now, UpdatedAt = now,
                }
            }
        });
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("data").GetProperty("recordsUpserted").GetInt32());
    }

    /// <summary>
    /// cross-family skip（terminal）：同一条记录 Id 曾同步到家庭 F_A，
    /// 换绑/另一家庭的用户再 Push 时不得 LWW 覆盖原家庭云端数据。
    /// </summary>
    [Fact]
    public async Task Push_ExistingRecordInOtherFamily_TerminalSkip()
    {
        using var factory = NewFactory();
        var userA = await NewAuthClientAsync(factory, "sync_cf_a_" + Guid.NewGuid().ToString("N")[..6]);
        var userB = await NewAuthClientAsync(factory, "sync_cf_b_" + Guid.NewGuid().ToString("N")[..6]);
        var babyIdA = await CreateBabyAsync(userA, "A的宝宝");
        var babyIdB = await CreateBabyAsync(userB, "B的宝宝");
        var meA = await userA.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var uidA = meA.GetProperty("data").GetProperty("id").GetString()!;

        // A push 一条记录（进入 A 的家庭）
        var t1 = DateTime.UtcNow;
        var recId = Guid.NewGuid().ToString("N");
        await userA.PostAsJsonAsync("/api/sync/push", new SyncBatchRequest
        {
            Records = new()
            {
                new SyncRecordItem
                {
                    Id = recId, UserId = uidA, BabyId = babyIdA,
                    RecordType = "feed", RecordDate = DateTime.Today, RecordTime = t1,
                    AmountMl = 100, PayloadJson = "{}", CreatedAt = t1, UpdatedAt = t1,
                }
            }
        });

        // B push 同 Id 记录（挂到 B 自己家庭的 baby，UpdatedAt 更新）——
        // 服务端发现 existing.FamilyId(A家庭) ≠ B 当前家庭 → terminal skip，不覆盖
        var t2 = t1.AddSeconds(30);
        var resp = await userB.PostAsJsonAsync("/api/sync/push", new SyncBatchRequest
        {
            Records = new()
            {
                new SyncRecordItem
                {
                    Id = recId, UserId = "uid-b", BabyId = babyIdB,
                    RecordType = "feed", RecordDate = DateTime.Today, RecordTime = t2,
                    AmountMl = 999, PayloadJson = "{}", CreatedAt = t2, UpdatedAt = t2,
                }
            }
        });
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data");
        Assert.Equal(0, data.GetProperty("recordsUpserted").GetInt32());
        var skipped = data.GetProperty("skippedForeignRecordIds");
        Assert.Equal(1, skipped.GetArrayLength());
        Assert.Equal(recId, skipped[0].GetString());

        // A pull 验证原数据未被覆盖（仍是 100）
        var pullResp = await userA.GetAsync("/api/sync/pull");
        var pullBody = await pullResp.Content.ReadFromJsonAsync<JsonElement>();
        var rec = pullBody.GetProperty("data").GetProperty("records")[0];
        Assert.Equal(100, rec.GetProperty("amountMl").GetInt32());
    }

    /// <summary>
    /// 家庭成员可见性：同一 Family 的另一成员 Pull 可拉到 owner 创建的 baby 与记录，
    /// 且 Pull 响应项携带 familyId（Family-centric 分区键）。
    /// </summary>
    [Fact]
    public async Task Pull_FamilyMemberSeesSharedDataWithFamilyId()
    {
        using var factory = NewFactory();
        var userA = await NewAuthClientAsync(factory, "sync_fm_a_" + Guid.NewGuid().ToString("N")[..6]);
        var userB = await NewAuthClientAsync(factory, "sync_fm_b_" + Guid.NewGuid().ToString("N")[..6]);
        var babyIdA = await CreateBabyAsync(userA, "A的宝宝");
        var meA = await userA.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var uidA = meA.GetProperty("data").GetProperty("id").GetString()!;
        var meB = await userB.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var uidB = meB.GetProperty("data").GetProperty("id").GetString()!;

        // A push 一条记录
        var now = DateTime.UtcNow;
        await userA.PostAsJsonAsync("/api/sync/push", new SyncBatchRequest
        {
            Records = new()
            {
                new SyncRecordItem
                {
                    Id = Guid.NewGuid().ToString("N"), UserId = uidA, BabyId = babyIdA,
                    RecordType = "feed", RecordDate = DateTime.Today, RecordTime = now,
                    AmountMl = 80, PayloadJson = "{}", CreatedAt = now, UpdatedAt = now,
                }
            }
        });

        // 把 B 加入 A 的家庭（Member）——直接操作 DbContext 布置（成员邀请 API 属阶段 3）
        string familyIdA;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChildNotesDbContext>();
            familyIdA = await db.Babies.Where(b => b.Id == babyIdA).Select(b => b.FamilyId).FirstAsync();
            db.FamilyMembers.Add(new ChildNotes.Core.Entities.FamilyMember
            {
                Id = Guid.NewGuid().ToString("N"),
                FamilyId = familyIdA,
                UserId = uidB,
                Role = ChildNotes.Core.Constants.StatusConstants.FamilyMemberRole.Member,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        // B pull：应看到 A 的 baby（同家庭共享），响应项携带 familyId
        var resp = await userB.GetAsync("/api/sync/pull");
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var babies = body.GetProperty("data").GetProperty("babies");
        Assert.Equal(1, babies.GetArrayLength());
        Assert.Equal(familyIdA, babies[0].GetProperty("familyId").GetString());
        var records = body.GetProperty("data").GetProperty("records");
        Assert.Equal(1, records.GetArrayLength());
        Assert.Equal(familyIdA, records[0].GetProperty("familyId").GetString());
        Assert.Equal(80, records[0].GetProperty("amountMl").GetInt32());
    }

    [Fact]
    public async Task Push_BabyDeletedFlagPropagatesToServer()
    {
        using var factory = NewFactory();
        var client = await NewAuthClientAsync(factory, "sync_del_" + Guid.NewGuid().ToString("N")[..6]);
        var babyId = await CreateBabyAsync(client);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var uid = me.GetProperty("data").GetProperty("id").GetString()!;

        // 等待 1 秒确保 push 的 UpdatedAt 严格新于创建时
        await Task.Delay(1000);
        var pushUpdatedAt = DateTime.UtcNow.AddSeconds(60); // 远比服务端新
        var resp = await client.PostAsJsonAsync("/api/sync/push", new SyncBatchRequest
        {
            Babies = new()
            {
                new SyncBabyItem
                {
                    Id = babyId, UserId = uid, Name = "小宝",
                    Avatar = "", Gender = "boy",
                    BirthDate = new DateTime(2025, 1, 1),
                    Deleted = true,
                    CreatedAt = pushUpdatedAt.AddSeconds(-60),
                    UpdatedAt = pushUpdatedAt,
                }
            }
        });
        var respBody = await resp.Content.ReadAsStringAsync();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("data").GetProperty("babiesUpserted").GetInt32());

        // pull 验证 Deleted=true
        var pullResp = await client.GetAsync("/api/sync/pull");
        var pullBody = await pullResp.Content.ReadFromJsonAsync<JsonElement>();
        var babies = pullBody.GetProperty("data").GetProperty("babies");
        Assert.Equal(1, babies.GetArrayLength());
        Assert.True(babies[0].GetProperty("deleted").GetBoolean());
    }

    [Fact]
    public async Task Push_MilestoneLWWConflict_ResolvesByUpdatedAt()
    {
        using var factory = NewFactory();
        var client = await NewAuthClientAsync(factory, "sync_ms_" + Guid.NewGuid().ToString("N")[..6]);
        var babyId = await CreateBabyAsync(client);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var uid = me.GetProperty("data").GetProperty("id").GetString()!;

        var t1 = DateTime.UtcNow;
        var msId = Guid.NewGuid().ToString("N");
        await client.PostAsJsonAsync("/api/sync/push", new SyncBatchRequest
        {
            Milestones = new()
            {
                new SyncMilestoneItem
                {
                    Id = msId, UserId = uid, BabyId = babyId,
                    Title = "第一次笑", Content = "v1",
                    RecordDate = DateTime.Today, PhotosJson = "[]",
                    CreatedAt = t1, UpdatedAt = t1,
                }
            }
        });

        // 较旧的 UpdatedAt 不应覆盖
        var t0 = t1.AddSeconds(-10);
        var resp = await client.PostAsJsonAsync("/api/sync/push", new SyncBatchRequest
        {
            Milestones = new()
            {
                new SyncMilestoneItem
                {
                    Id = msId, UserId = uid, BabyId = babyId,
                    Title = "第一次笑", Content = "v0-older",
                    RecordDate = DateTime.Today, PhotosJson = "[]",
                    CreatedAt = t0, UpdatedAt = t0,
                }
            }
        });
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("data").GetProperty("milestonesUpserted").GetInt32());

        // 较新的 UpdatedAt 应覆盖
        var t2 = t1.AddSeconds(10);
        var resp2 = await client.PostAsJsonAsync("/api/sync/push", new SyncBatchRequest
        {
            Milestones = new()
            {
                new SyncMilestoneItem
                {
                    Id = msId, UserId = uid, BabyId = babyId,
                    Title = "第一次笑", Content = "v2-newer",
                    RecordDate = DateTime.Today, PhotosJson = "[]",
                    CreatedAt = t1, UpdatedAt = t2,
                }
            }
        });
        var body2 = await resp2.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body2.GetProperty("data").GetProperty("milestonesUpserted").GetInt32());

        var pullResp = await client.GetAsync("/api/sync/pull");
        var pullBody = await pullResp.Content.ReadFromJsonAsync<JsonElement>();
        var ms = pullBody.GetProperty("data").GetProperty("milestones")[0];
        Assert.Equal("v2-newer", ms.GetProperty("content").GetString());
    }
}
