using Microsoft.Data.Sqlite;
using ChildNotes.Shared.Sync;

namespace ChildNotes.Data.Repositories;

/// <summary>
/// 家庭加入申请本地仓储（Pull-only）。
/// 客户端接收服务端下发的申请记录，用于：
/// 1) owner 端展示待审列表并触发 InAppMessage 通知有新申请
/// 2) 申请人端感知审批结果（approved/rejected）并触发 InAppMessage 通知
/// 不上送（Push 忽略），所有写入通过同步 Pull LWW 合并。
/// </summary>
public sealed class FamilyJoinRequestRepository : BaseRepository
{
    public FamilyJoinRequestRepository(DbConnectionFactory factory) : base(factory) { }

    /// <summary>
    /// 以 LWW（updated_at 比较）合并远端下发的申请记录。返回是否实际写入。
    /// </summary>
    public bool UpsertFromSync(SyncFamilyJoinRequestItem item, SqliteConnection conn, SqliteTransaction? tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO family_join_request
                (id, baby_id, applicant_user_id, role_code, role_name, status, processed_at, created_at, updated_at)
            VALUES
                (@id, @bid, @auid, @rc, @rn, @st, @pa, @c, @u)
            ON CONFLICT(id) DO UPDATE SET
                baby_id = excluded.baby_id,
                applicant_user_id = excluded.applicant_user_id,
                role_code = excluded.role_code,
                role_name = excluded.role_name,
                status = excluded.status,
                processed_at = excluded.processed_at,
                updated_at = excluded.updated_at
            WHERE excluded.updated_at > family_join_request.updated_at";
        cmd.Add("@id", item.Id)
           .Add("@bid", item.BabyId)
           .Add("@auid", item.ApplicantUserId)
           .Add("@rc", item.RoleCode)
           .Add("@rn", item.RoleName)
           .Add("@st", item.Status)
           .Add("@pa", (object?)item.ProcessedAt ?? DBNull.Value)
           .AddUtc("@c", item.CreatedAt)
           .AddUtc("@u", item.UpdatedAt);
        return cmd.ExecuteNonQuery() > 0;
    }

    /// <summary>查询本地全部申请记录（按 updated_at 倒序）。</summary>
    public List<SyncFamilyJoinRequestItem> GetAll()
    {
        const string sql = @"
SELECT id, baby_id, applicant_user_id, role_code, role_name, status, processed_at, created_at, updated_at
FROM family_join_request
ORDER BY updated_at DESC;";
        return Query(sql, _ => { }, Map);
    }

    /// <summary>按 Id 查询单条申请记录（用于状态变化比较）。</summary>
    public SyncFamilyJoinRequestItem? FindById(string id)
    {
        const string sql = @"
SELECT id, baby_id, applicant_user_id, role_code, role_name, status, processed_at, created_at, updated_at
FROM family_join_request
WHERE id = @id;";
        return QueryFirstOrDefault(sql, cmd => cmd.Add("id", id), Map);
    }

    private static SyncFamilyJoinRequestItem Map(SqliteDataReader r)
    {
        return new SyncFamilyJoinRequestItem
        {
            Id = r.GetString(0),
            BabyId = r.GetString(1),
            ApplicantUserId = r.GetString(2),
            RoleCode = r.GetString(3),
            RoleName = r.GetString(4),
            Status = r.GetString(5),
            ProcessedAt = r.IsDBNull(6) ? null : DateTimeExtensions.ParseDb(r.GetString(6)),
            CreatedAt = DateTimeExtensions.ParseDb(r.GetString(7)),
            UpdatedAt = DateTimeExtensions.ParseDb(r.GetString(8)),
        };
    }
}
