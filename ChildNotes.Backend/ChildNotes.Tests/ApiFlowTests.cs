using ChildNotes.Core.Constants;
using ChildNotes.Core.Dtos;
using ChildNotes.Core.Services;
using ChildNotes.Shared.Constants;
using ChildNotes.Shared.Dtos;
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
/// 集成测试：每个测试方法独立 factory + 独立内存数据库，保证隔离
/// </summary>
public class ApiFlowTests
{
    private static ApiFactory NewFactory() => new();

    private static async Task<HttpClient> NewAuthClientAsync(ApiFactory factory, string username)
    {
        // 邮箱验证码登录：username 作为 email 前缀，生成唯一邮箱
        var email = $"{username}@test.local";
        var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/send-code", new SendCodeRequest { Email = email });
        resp.EnsureSuccessStatusCode();
        // 从 stub EmailSender 取出验证码
        var code = factory.GetLastCode(email) ?? throw new InvalidOperationException($"未捕获到 {email} 的验证码");
        var verifyResp = await client.PostAsJsonAsync("/api/auth/verify-code",
            new VerifyCodeRequest { Email = email, Code = code });
        verifyResp.EnsureSuccessStatusCode();
        var body = await verifyResp.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("data").GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    [Fact]
    public async Task VerifyCode_NewEmail_CreatesUserAndReturnsTokens()
    {
        using var factory = NewFactory();
        var client = factory.CreateClient();
        var email = "user_reg_" + Guid.NewGuid().ToString("N")[..6] + "@test.local";
        var resp = await client.PostAsJsonAsync("/api/auth/send-code", new SendCodeRequest { Email = email });
        Assert.True(resp.IsSuccessStatusCode, await resp.Content.ReadAsStringAsync());
        var code = factory.GetLastCode(email)!;
        var verifyResp = await client.PostAsJsonAsync("/api/auth/verify-code",
            new VerifyCodeRequest { Email = email, Code = code });
        var verifyBody = await verifyResp.Content.ReadAsStringAsync();
        Assert.True(verifyResp.IsSuccessStatusCode, verifyBody);
        var body = await verifyResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("000000", body.GetProperty("state").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("data").GetProperty("accessToken").GetString()));
        Assert.False(string.IsNullOrEmpty(body.GetProperty("data").GetProperty("refreshToken").GetString()));
        Assert.True(body.GetProperty("data").GetProperty("newUser").GetBoolean());
    }

    [Fact]
    public async Task VerifyCode_WrongCode_Fails()
    {
        using var factory = NewFactory();
        var client = factory.CreateClient();
        var email = "user_login_" + Guid.NewGuid().ToString("N")[..6] + "@test.local";
        await client.PostAsJsonAsync("/api/auth/send-code", new SendCodeRequest { Email = email });
        // 用错误验证码
        var resp = await client.PostAsJsonAsync("/api/auth/verify-code",
            new VerifyCodeRequest { Email = email, Code = "000000" });
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("000520", body.GetProperty("state").GetString());
    }

    [Fact]
    public async Task Me_WithoutToken_Returns401()
    {
        using var factory = NewFactory();
        var client = factory.CreateClient();
        var resp = await client.GetAsync("/api/auth/me");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    /// <summary>登录并返回携带 Bearer 的客户端与 RefreshToken 明文（refresh 流程测试用）。</summary>
    private static async Task<(HttpClient client, string refreshToken)> NewAuthClientWithRefreshTokenAsync(
        ApiFactory factory, string username)
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
        var refreshToken = body.GetProperty("data").GetProperty("refreshToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new("Bearer",
            body.GetProperty("data").GetProperty("accessToken").GetString()!);
        return (client, refreshToken);
    }

    /// <summary>调用 /api/auth/refresh，成功时返回新 token 对。</summary>
    private static async Task<(string accessToken, string refreshToken)> RefreshTokensAsync(
        HttpClient client, string refreshToken)
    {
        var resp = await client.PostAsJsonAsync("/api/auth/refresh",
            new RefreshRequest { RefreshToken = refreshToken });
        var body = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, body);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return (
            json.GetProperty("data").GetProperty("accessToken").GetString()!,
            json.GetProperty("data").GetProperty("refreshToken").GetString()!);
    }

    [Fact]
    public async Task Refresh_Rotation_IssuesNewTokenPair()
    {
        using var factory = NewFactory();
        var (client, refreshToken) = await NewAuthClientWithRefreshTokenAsync(
            factory, "rf1_" + Guid.NewGuid().ToString("N")[..6]);

        var (newAccess, newRefresh) = await RefreshTokensAsync(client, refreshToken);
        Assert.False(string.IsNullOrEmpty(newAccess));
        Assert.False(string.IsNullOrEmpty(newRefresh));
        Assert.NotEqual(refreshToken, newRefresh);

        // 新 AccessToken 可正常访问鉴权接口
        client.DefaultRequestHeaders.Authorization = new("Bearer", newAccess);
        var meResp = await client.GetAsync("/api/auth/me");
        Assert.True(meResp.IsSuccessStatusCode, await meResp.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// 宽限期内重放已撤销的旧 token：应换取新 token 而非 401。
    /// 场景：Rotation 后客户端因网络超时/进程中断未保存新 token，
    /// 只能重放旧 token——401 会触发客户端软登出，造成"掉线"故障。
    /// </summary>
    [Fact]
    public async Task Refresh_ReplayedRevokedToken_WithinGrace_ReturnsNewToken()
    {
        using var factory = NewFactory();
        var (client, refreshToken) = await NewAuthClientWithRefreshTokenAsync(
            factory, "rf2_" + Guid.NewGuid().ToString("N")[..6]);

        // 第一次 refresh 成功：旧 token 已被服务端撤销（模拟客户端未收到响应）
        await RefreshTokensAsync(client, refreshToken);

        // 宽限期内（默认 120s）重放旧 token：应成功再签发新 token 对
        var (retryAccess, _) = await RefreshTokensAsync(client, refreshToken);
        Assert.False(string.IsNullOrEmpty(retryAccess));

        // 重试拿到的新 AccessToken 也可用
        client.DefaultRequestHeaders.Authorization = new("Bearer", retryAccess);
        var meResp = await client.GetAsync("/api/auth/me");
        Assert.True(meResp.IsSuccessStatusCode, await meResp.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// 宽限期外重放已撤销的旧 token，且继任者仍活跃（孤儿 token）：
    /// 应走前驱链恢复，签发新 token 对而非 401（隔夜掉线自愈）。
    /// 场景：Rotation 响应丢失（进程在保存新 token 前被杀），次日客户端重放旧 token。
    /// </summary>
    [Fact]
    public async Task Refresh_ReplayedRevokedToken_AfterGrace_WithActiveSuccessor_Recovers()
    {
        using var factory = NewFactory();
        var (client, refreshToken) = await NewAuthClientWithRefreshTokenAsync(
            factory, "rf3_" + Guid.NewGuid().ToString("N")[..6]);

        // 第一次 refresh：旧 token 已被服务端撤销，新 token（继任者）仍活跃（模拟响应丢失）
        await RefreshTokensAsync(client, refreshToken);

        // 所有 token 时间整体回拨 1 小时：撤销时间出宽限期，且继任者 CreatedAt 与
        // 撤销时间的 ~0.3s 链接关系保持不变（真实前驱-继任结构）
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChildNotesDbContext>();
            foreach (var t in db.RefreshTokens.ToList())
            {
                t.CreatedAt = t.CreatedAt.AddHours(-1);
                if (t.RevokedAt != null) t.RevokedAt = t.RevokedAt.Value.AddHours(-1);
                t.ExpiresAt = t.ExpiresAt.AddHours(-1);
            }
            await db.SaveChangesAsync();
        }

        // 宽限期外重放旧 token：应自愈签发新 token 对
        var (recoveredAccess, _) = await RefreshTokensAsync(client, refreshToken);
        Assert.False(string.IsNullOrEmpty(recoveredAccess));

        client.DefaultRequestHeaders.Authorization = new("Bearer", recoveredAccess);
        var meResp = await client.GetAsync("/api/auth/me");
        Assert.True(meResp.IsSuccessStatusCode, await meResp.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// 前驱恢复的一次性保证：同一旧 token 恢复一次后（继任者被撤销），
    /// 二次重放应返回 401（防盗用重放）。
    /// </summary>
    [Fact]
    public async Task Refresh_ReplayedRevokedToken_RecoveryIsOneShot()
    {
        using var factory = NewFactory();
        var (client, refreshToken) = await NewAuthClientWithRefreshTokenAsync(
            factory, "rf4_" + Guid.NewGuid().ToString("N")[..6]);

        await RefreshTokensAsync(client, refreshToken);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChildNotesDbContext>();
            foreach (var t in db.RefreshTokens.ToList())
            {
                t.CreatedAt = t.CreatedAt.AddHours(-1);
                if (t.RevokedAt != null) t.RevokedAt = t.RevokedAt.Value.AddHours(-1);
                t.ExpiresAt = t.ExpiresAt.AddHours(-1);
            }
            await db.SaveChangesAsync();
        }

        // 第一次重放：恢复成功
        await RefreshTokensAsync(client, refreshToken);

        // 第二次重放同一旧 token：继任者已被恢复动作撤销，无活跃继任者 → 401
        var resp = await client.PostAsJsonAsync("/api/auth/refresh",
            new RefreshRequest { RefreshToken = refreshToken });
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    /// <summary>
    /// 宽限期外重放已撤销的旧 token，且继任者也已撤销（链条已前进，设备持有更新 token）：
    /// 应返回 401（防盗用重放）。
    /// </summary>
    [Fact]
    public async Task Refresh_ReplayedRevokedToken_AfterGrace_WithRevokedSuccessor_Returns401()
    {
        using var factory = NewFactory();
        var (client, refreshToken) = await NewAuthClientWithRefreshTokenAsync(
            factory, "rf5_" + Guid.NewGuid().ToString("N")[..6]);

        // 第一次 refresh：T0 撤销 → S1 活跃
        var (_, s1) = await RefreshTokensAsync(client, refreshToken);

        // 时间整体回拨出宽限期，保持前驱-继任链接关系
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChildNotesDbContext>();
            foreach (var t in db.RefreshTokens.ToList())
            {
                t.CreatedAt = t.CreatedAt.AddHours(-1);
                if (t.RevokedAt != null) t.RevokedAt = t.RevokedAt.Value.AddHours(-1);
                t.ExpiresAt = t.ExpiresAt.AddHours(-1);
            }
            await db.SaveChangesAsync();
        }

        // 回拨后再做第二次 refresh（真实时间）：T0 → S1（撤销）→ S2，设备持有 S2。
        // S1 的撤销发生在"现在"（宽限期内），但 T0 的继任者窗口在一小时前且 S1 已撤销。
        await RefreshTokensAsync(client, s1);

        // 重放最初的 T0：无活跃继任者（S1 已撤销、S2 不在窗口内）→ 401
        var resp = await client.PostAsJsonAsync("/api/auth/refresh",
            new RefreshRequest { RefreshToken = refreshToken });
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    /// <summary>
    /// 旧格式 token（token_hash_fast 为 null，2026-09 前签发）：应走 PBKDF2
    /// 慢路径回退验证并成功换新 token 对。
    /// </summary>
    [Fact]
    public async Task Refresh_LegacyToken_WithoutFastHash_StillWorks()
    {
        using var factory = NewFactory();
        var (client, refreshToken) = await NewAuthClientWithRefreshTokenAsync(
            factory, "rf6_" + Guid.NewGuid().ToString("N")[..6]);

        // 模拟旧数据：清空 fast 哈希列（部署 fast-hash 前签发的 token 均如此）
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChildNotesDbContext>();
            foreach (var t in db.RefreshTokens.ToList())
                t.TokenHashFast = null;
            await db.SaveChangesAsync();
        }

        // 旧格式 token 刷新：走 PBKDF2 回退路径，应成功
        var (newAccess, newRefresh) = await RefreshTokensAsync(client, refreshToken);
        Assert.False(string.IsNullOrEmpty(newAccess));
        Assert.False(string.IsNullOrEmpty(newRefresh));

        // 新签发的 token 带有 fast 哈希，再刷一次走快路径也应成功
        var (access2, _) = await RefreshTokensAsync(client, newRefresh);
        Assert.False(string.IsNullOrEmpty(access2));

        client.DefaultRequestHeaders.Authorization = new("Bearer", access2);
        var meResp = await client.GetAsync("/api/auth/me");
        Assert.True(meResp.IsSuccessStatusCode, await meResp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CreateBaby_AutoCreatesOwnerMember()
    {
        using var factory = NewFactory();
        var client = await NewAuthClientAsync(factory, "user_baby_" + Guid.NewGuid().ToString("N")[..6]);
        var resp = await client.PostAsJsonAsync("/api/baby/add", new CreateBabyRequest
        {
            Name = "小宝",
            Gender = "boy",
            BirthDate = new DateTime(2025, 1, 1),
        });
        var respBody = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, respBody);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("000000", body.GetProperty("state").GetString());
        var baby = body.GetProperty("data");
        Assert.Equal("小宝", baby.GetProperty("name").GetString());

        // 列出家庭成员应该有 1 个 owner（自己）
        var famResp = await client.GetAsync("/api/baby/family/members");
        var famRespBody = await famResp.Content.ReadAsStringAsync();
        Assert.True(famResp.IsSuccessStatusCode, famRespBody);
        var famBody = await famResp.Content.ReadFromJsonAsync<JsonElement>();
        var families = famBody.GetProperty("data");
        Assert.Equal(1, families.GetArrayLength());
        var members = families[0].GetProperty("members");
        Assert.Equal(1, members.GetArrayLength());
        Assert.True(members[0].GetProperty("owner").GetBoolean());
        Assert.True(members[0].GetProperty("mine").GetBoolean());
    }

    /// <summary>通过 join-request 审批流加入家庭：member 提交申请，owner 批准。</summary>
    private static async Task JoinViaApprovalFlowAsync(
        HttpClient member, HttpClient owner, string babyId, string roleCode)
    {
        var reqResp = await member.PostAsJsonAsync("/api/baby/family/join-request",
            new JoinFamilyRequest { BabyId = babyId, RoleCode = roleCode });
        var reqBody = await reqResp.Content.ReadAsStringAsync();
        Assert.True(reqResp.IsSuccessStatusCode, reqBody);
        var requestId = (await reqResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetString()!;

        var approveResp = await owner.PostAsJsonAsync("/api/baby/family/join-request/process",
            new ProcessJoinRequestDto { RequestId = requestId, Approve = true });
        var approveBody = await approveResp.Content.ReadAsStringAsync();
        Assert.True(approveResp.IsSuccessStatusCode, approveBody);
    }

    [Fact]
    public async Task JoinFamily_AddsMemberToAllOwnerBabies()
    {
        using var factory = NewFactory();
        // 用户 A 创建两个宝宝
        var ownerA = await NewAuthClientAsync(factory, "ownerA_" + Guid.NewGuid().ToString("N")[..6]);
        var baby1Resp = await ownerA.PostAsJsonAsync("/api/baby/add", new CreateBabyRequest { Name = "大宝" });
        var baby1RespBody = await baby1Resp.Content.ReadAsStringAsync();
        Assert.True(baby1Resp.IsSuccessStatusCode, baby1RespBody);
        var baby1Id = (await baby1Resp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetString()!;
        await ownerA.PostAsJsonAsync("/api/baby/add", new CreateBabyRequest { Name = "二宝" });

        // 用户 B 注册
        var userB = await NewAuthClientAsync(factory, "userB_" + Guid.NewGuid().ToString("N")[..6]);

        // B 通过 baby1Id 走审批流加入家庭（owner 批准）
        await JoinViaApprovalFlowAsync(userB, ownerA, baby1Id, "mother");

        // B 查看家庭成员，应能看到 A 名下两个宝宝
        var famResp = await userB.GetAsync("/api/baby/family/members");
        var famRespBody = await famResp.Content.ReadAsStringAsync();
        Assert.True(famResp.IsSuccessStatusCode, famRespBody);
        var famBody = await famResp.Content.ReadFromJsonAsync<JsonElement>();
        var families = famBody.GetProperty("data");
        Assert.Equal(2, families.GetArrayLength());
    }

    /// <summary>
    /// 审批通过必须补写 FamilyMember（家庭分区）：否则申请人"当前家庭"仍是自建家庭，
    /// 同步 push/pull 按 FamilyId 分区永远拉不到/推不了共享家庭数据（家庭共享主链路断裂）。
    /// 验证：① DB 有 FamilyMember 行；② 服务端当前家庭解析切到共享家庭；
    /// ③ 申请人 /api/baby/current（按家庭过滤）能返回 owner 的宝宝。
    /// </summary>
    [Fact]
    public async Task JoinApproval_WritesFamilyMember_SyncPartitionSwitchesToSharedFamily()
    {
        using var factory = NewFactory();
        var ownerA = await NewAuthClientAsync(factory, "fOwnA_" + Guid.NewGuid().ToString("N")[..6]);
        var babyResp = await ownerA.PostAsJsonAsync("/api/baby/add", new CreateBabyRequest { Name = "大宝" });
        var babyId = (await babyResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetString()!;

        var userBName = "fJoinB_" + Guid.NewGuid().ToString("N")[..6];
        var userB = await NewAuthClientAsync(factory, userBName);
        await JoinViaApprovalFlowAsync(userB, ownerA, babyId, "mother");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChildNotesDbContext>();
            // email 落库时已 ToLowerInvariant 规范化，查询须用小写
            var bUser = await db.AppUsers.FirstAsync(u => u.Email == $"{userBName}@test.local".ToLowerInvariant());
            var baby = await db.Babies.FirstAsync(b => b.Id == babyId);

            // ① FamilyMember 行已补写（Role=member）
            var fm = await db.FamilyMembers.FirstOrDefaultAsync(
                x => x.FamilyId == baby.FamilyId && x.UserId == bUser.Id);
            Assert.NotNull(fm);
            Assert.Equal(StatusConstants.FamilyMemberRole.Member, fm!.Role);

            // ② 服务端"当前家庭"解析已切换到共享家庭（同步分区键）
            var familyService = scope.ServiceProvider.GetRequiredService<IFamilyService>();
            var currentFid = await familyService.GetCurrentFamilyIdAsync(bUser.Id);
            Assert.Equal(baby.FamilyId, currentFid);
        }

        // ③ 家庭过滤的默认宝宝接口返回 owner 的宝宝（分区切换的端到端表现）
        var curResp = await userB.GetAsync("/api/baby/current");
        var curBody = await curResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("000000", curBody.GetProperty("state").GetString());
        Assert.Equal("大宝", curBody.GetProperty("data").GetProperty("name").GetString());
    }

    /// <summary>
    /// 被移除者在本家庭已无 active BabyMember 时，FamilyMember 行须一并删除：
    /// 否则其"当前家庭"仍解析为共享家庭，可继续以该分区拉取家庭数据（隐私泄漏）。
    /// </summary>
    [Fact]
    public async Task RemoveMember_LastBabyRemoved_DeletesFamilyMemberPartition()
    {
        using var factory = NewFactory();
        var ownerA = await NewAuthClientAsync(factory, "rmOwnA_" + Guid.NewGuid().ToString("N")[..6]);
        var babyResp = await ownerA.PostAsJsonAsync("/api/baby/add", new CreateBabyRequest { Name = "大宝" });
        var babyRespBody = await babyResp.Content.ReadAsStringAsync();
        Assert.True(babyResp.IsSuccessStatusCode, babyRespBody);
        var babyId = (await babyResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetString()!;

        var userBName = "rmJoinB_" + Guid.NewGuid().ToString("N")[..6];
        var userB = await NewAuthClientAsync(factory, userBName);
        await JoinViaApprovalFlowAsync(userB, ownerA, babyId, "father");

        // owner 移除 B（DELETE 带 body）
        string targetUserId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChildNotesDbContext>();
            targetUserId = (await db.AppUsers.FirstAsync(u => u.Email == $"{userBName}@test.local".ToLowerInvariant())).Id;
        }
        var removeReq = new HttpRequestMessage(HttpMethod.Delete, "/api/baby/family/member")
        {
            Content = JsonContent.Create(new RemoveMemberRequest { BabyId = babyId, TargetUserId = targetUserId }),
        };
        var removeResp = await ownerA.SendAsync(removeReq);
        var removeBody = await removeResp.Content.ReadAsStringAsync();
        Assert.True(removeResp.IsSuccessStatusCode, removeBody);

        // FamilyMember 行已删除：B 的当前家庭回落到自建家庭
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChildNotesDbContext>();
            var baby = await db.Babies.FirstAsync(b => b.Id == babyId);
            var fm = await db.FamilyMembers.FirstOrDefaultAsync(
                x => x.FamilyId == baby.FamilyId && x.UserId == targetUserId);
            Assert.Null(fm);

            var familyService = scope.ServiceProvider.GetRequiredService<IFamilyService>();
            var currentFid = await familyService.GetCurrentFamilyIdAsync(targetUserId);
            Assert.NotEqual(baby.FamilyId, currentFid);
        }

        // 家庭过滤的默认宝宝接口不再返回 owner 的宝宝
        var curResp = await userB.GetAsync("/api/baby/current");
        var curBody = await curResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("000000", curBody.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, curBody.GetProperty("data").ValueKind);
    }

    [Fact]
    public async Task UpdateMyRole_OnlyAffectsSelf()
    {
        using var factory = NewFactory();
        var owner = await NewAuthClientAsync(factory, "roleOwner_" + Guid.NewGuid().ToString("N")[..6]);
        var babyResp = await owner.PostAsJsonAsync("/api/baby/add", new CreateBabyRequest { Name = "宝" });
        var babyRespBody = await babyResp.Content.ReadAsStringAsync();
        Assert.True(babyResp.IsSuccessStatusCode, babyRespBody);
        var babyId = (await babyResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetString()!;

        var member = await NewAuthClientAsync(factory, "roleMember_" + Guid.NewGuid().ToString("N")[..6]);
        await JoinViaApprovalFlowAsync(member, owner, babyId, "uncle");

        // member 修改自己的角色
        var updResp = await member.PutAsJsonAsync("/api/baby/family/my-role", new UpdateBabyMemberRoleRequest
        {
            BabyId = babyId,
            RoleCode = "grandpa",
        });
        var updRespBody = await updResp.Content.ReadAsStringAsync();
        Assert.True(updResp.IsSuccessStatusCode, updRespBody);
        var updBody = await updResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("爷爷", updBody.GetProperty("data").GetProperty("roleName").GetString());
    }

    [Fact]
    public async Task AddRecord_AndRetrieveToday()
    {
        using var factory = NewFactory();
        var client = await NewAuthClientAsync(factory, "rec_" + Guid.NewGuid().ToString("N")[..6]);
        var babyResp = await client.PostAsJsonAsync("/api/baby/add", new CreateBabyRequest { Name = "宝" });
        Assert.True(babyResp.IsSuccessStatusCode, await babyResp.Content.ReadAsStringAsync());

        var feedResp = await client.PostAsJsonAsync("/api/records/feed", new FeedRecordDto
        {
            Time = DateTime.Now.ToString("O"),
            Type = FeedType.Bottle,
            Amount = 120,
        });
        var feedRespBody = await feedResp.Content.ReadAsStringAsync();
        Assert.True(feedResp.IsSuccessStatusCode, feedRespBody);

        var todayResp = await client.GetAsync("/api/records/today");
        Assert.True(todayResp.IsSuccessStatusCode, await todayResp.Content.ReadAsStringAsync());
        var todayBody = await todayResp.Content.ReadFromJsonAsync<JsonElement>();
        var recordsByType = todayBody.GetProperty("data").GetProperty("recordsByType");
        Assert.True(recordsByType.TryGetProperty("feed", out _));
    }

    [Fact]
    public async Task DeleteRecord_LogicalDelete()
    {
        using var factory = NewFactory();
        var client = await NewAuthClientAsync(factory, "del_" + Guid.NewGuid().ToString("N")[..6]);
        await client.PostAsJsonAsync("/api/baby/add", new CreateBabyRequest { Name = "宝" });
        var feedResp = await client.PostAsJsonAsync("/api/records/feed", new FeedRecordDto
        {
            Time = DateTime.Now.ToString("O"),
            Type = FeedType.Bottle,
            Amount = 100,
        });
        var feedRespBody = await feedResp.Content.ReadAsStringAsync();
        Assert.True(feedResp.IsSuccessStatusCode, feedRespBody);
        var feedBody = await feedResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = feedBody.GetProperty("data").GetProperty("id").GetString()!;

        var delResp = await client.DeleteAsync($"/api/records/{id}");
        Assert.True(delResp.IsSuccessStatusCode, await delResp.Content.ReadAsStringAsync());

        // 再次查询今日记录，feed 应该没有了
        var todayResp = await client.GetAsync("/api/records/today");
        Assert.True(todayResp.IsSuccessStatusCode, await todayResp.Content.ReadAsStringAsync());
        var todayBody = await todayResp.Content.ReadFromJsonAsync<JsonElement>();
        var recordsByType = todayBody.GetProperty("data").GetProperty("recordsByType");
        Assert.False(recordsByType.TryGetProperty("feed", out _));
    }

    [Fact]
    public async Task AccessOtherBaby_Forbidden()
    {
        using var factory = NewFactory();
        var ownerA = await NewAuthClientAsync(factory, "fo_" + Guid.NewGuid().ToString("N")[..6]);
        var babyResp = await ownerA.PostAsJsonAsync("/api/baby/add", new CreateBabyRequest { Name = "宝A" });
        var babyRespBody = await babyResp.Content.ReadAsStringAsync();
        Assert.True(babyResp.IsSuccessStatusCode, babyRespBody);
        var babyAId = (await babyResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetString()!;

        var userB = await NewAuthClientAsync(factory, "fb_" + Guid.NewGuid().ToString("N")[..6]);
        // B 没加入家庭，直接查 A 的宝宝今日记录
        var resp = await userB.GetAsync($"/api/records/today?babyId={babyAId}");
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("000520", body.GetProperty("state").GetString()); // 无权限
    }
}

/// <summary>
/// 测试用 WebApplicationFactory，替换为内存数据库
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    public string DbName { get; } = $"test-{Guid.NewGuid()}";

    /// <summary>测试用 Admin 密码（与 Program.cs 中开发环境回退值解耦）</summary>
    public const string TestAdminPassword = "test-admin-pass-123";

    /// <summary>测试用 Stub EmailSender：捕获最新验证码到邮箱索引（线程安全）</summary>
    private readonly TestEmailSender _emailSender = new();

    /// <summary>获取指定邮箱最后一次发送的验证码明文（仅测试 stub 场景）。</summary>
    public string? GetLastCode(string email) => _emailSender.GetLastCode(email);

    /// <summary>可选：支付宝回调测试用，自定义 MembershipOptions（如注入测试用支付宝公钥）。</summary>
    public Action<Core.Config.MembershipOptions>? ConfigureMembershipOptions { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            // 测试环境显式覆盖 Admin 密码，避免依赖 appsettings.json 默认值
            services.PostConfigure<Core.Config.AdminOptions>(opt =>
            {
                opt.InitPassword = TestAdminPassword;
            });
            // 支付宝回调测试：注入测试密钥等自定义配置
            if (ConfigureMembershipOptions is not null)
                services.PostConfigure(ConfigureMembershipOptions);
            // 测试环境覆盖 EmailAuth：缩短重发间隔，避免 60s 限流影响测试
            services.PostConfigure<Core.Config.EmailAuthOptions>(opt =>
            {
                opt.ResendIntervalSeconds = 0;
                opt.CodeTtlSeconds = 600;
            });
            // 测试环境放宽接口限流：默认 5 req/s 会让"用尽免费次数"类测试（连续 10+ 次调用）触发 429
            services.PostConfigure<Core.Config.RateLimitOptions>(opt =>
            {
                opt.MaxRequestsPerSecond = 1000;
                opt.BlacklistRequestsPerSecond = 2000;
            });
            // 替换 IEmailSender 为测试 Stub，避免真实 SMTP 调用
            services.RemoveAll<Core.Services.IEmailSender>();
            services.AddSingleton<Core.Services.IEmailSender>(_ => _emailSender);
            services.RemoveAll<DbContextOptions<ChildNotesDbContext>>();
            services.RemoveAll<ChildNotesDbContext>();
            services.AddDbContext<ChildNotesDbContext>(opt =>
                opt.UseInMemoryDatabase(DbName));
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ChildNotesDbContext>();
            db.Database.EnsureCreated();
        });
    }
}

/// <summary>
/// 测试用 EmailSender：从 HTML body 中提取 6 位验证码，按邮箱索引保存。
/// 验证码 HTML 模板由 AuthService 生成，含一个 letter-spacing:8px 的 div 包裹纯数字验证码。
/// </summary>
public class TestEmailSender : Core.Services.IEmailSender
{
    private readonly Dictionary<string, string> _codes = new();
    private readonly object _lock = new();

    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        // 从 HTML 中提取 6 位数字验证码（AuthService 模板固定 6 位）
        var match = System.Text.RegularExpressions.Regex.Match(htmlBody, @"\b(\d{6})\b");
        var code = match.Success ? match.Groups[1].Value : "000000";
        lock (_lock)
        {
            _codes[to.Trim().ToLowerInvariant()] = code;
        }
        return Task.CompletedTask;
    }

    public string? GetLastCode(string email)
    {
        lock (_lock)
        {
            return _codes.TryGetValue(email.Trim().ToLowerInvariant(), out var code) ? code : null;
        }
    }
}
