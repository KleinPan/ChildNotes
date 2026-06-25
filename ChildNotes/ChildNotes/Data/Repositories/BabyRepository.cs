using Microsoft.Data.Sqlite;
using ChildNotes.Models;

namespace ChildNotes.Data.Repositories;

public sealed class BabyRepository
{
    private readonly DbConnectionFactory _factory;

    public BabyRepository(DbConnectionFactory factory) => _factory = factory;

    public List<Baby> GetByUser(long userId)
    {
        var list = new List<Baby>();
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, user_id, name, avatar, gender, birth_date, created_at, updated_at FROM baby WHERE user_id = @u ORDER BY id";
        cmd.Parameters.AddWithValue("@u", userId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(MapBaby(r));
        return list;
    }

    public Baby? FindById(long id)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, user_id, name, avatar, gender, birth_date, created_at, updated_at FROM baby WHERE id = @i";
        cmd.Parameters.AddWithValue("@i", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? MapBaby(r) : null;
    }

    public long Insert(Baby baby)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO baby (user_id, name, avatar, gender, birth_date, created_at, updated_at)
            VALUES (@u, @n, @a, @g, @b, @c, @c); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@u", baby.UserId);
        cmd.Parameters.AddWithValue("@n", baby.Name);
        cmd.Parameters.AddWithValue("@a", (object?)baby.Avatar ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@g", (object?)baby.Gender ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@b", (object?)(baby.BirthDate?.ToString("yyyy-MM-dd")) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@c", DateTime.UtcNow.ToString("O"));
        return (long)cmd.ExecuteScalar()!;
    }

    public void Update(Baby baby)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE baby SET name=@n, avatar=@a, gender=@g, birth_date=@b, updated_at=@t WHERE id=@i";
        cmd.Parameters.AddWithValue("@n", baby.Name);
        cmd.Parameters.AddWithValue("@a", (object?)baby.Avatar ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@g", (object?)baby.Gender ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@b", (object?)(baby.BirthDate?.ToString("yyyy-MM-dd")) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@i", baby.Id);
        cmd.ExecuteNonQuery();
    }

    public List<BabyMember> GetMembers(long babyId)
    {
        var list = new List<BabyMember>();
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, baby_id, user_id, role_code, role_name, is_owner, status, created_at, updated_at FROM baby_member WHERE baby_id = @b AND status='active' ORDER BY is_owner DESC, id";
        cmd.Parameters.AddWithValue("@b", babyId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(MapMember(r));
        return list;
    }

    private static Baby MapBaby(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        UserId = r.GetInt64(1),
        Name = r.GetString(2),
        Avatar = r.IsDBNull(3) ? string.Empty : r.GetString(3),
        Gender = r.IsDBNull(4) ? string.Empty : r.GetString(4),
        BirthDate = r.IsDBNull(5) ? null : DateTime.Parse(r.GetString(5)),
        CreatedAt = DateTime.Parse(r.GetString(6)),
        UpdatedAt = DateTime.Parse(r.GetString(7)),
    };

    public void InsertMember(BabyMember member)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO baby_member (baby_id, user_id, role_code, role_name, is_owner, status, created_at, updated_at)
            VALUES (@b, @u, @rc, @rn, @o, @s, @c, @c); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@b", member.BabyId);
        cmd.Parameters.AddWithValue("@u", member.UserId);
        cmd.Parameters.AddWithValue("@rc", member.RoleCode);
        cmd.Parameters.AddWithValue("@rn", member.RoleName);
        cmd.Parameters.AddWithValue("@o", member.IsOwner ? 1 : 0);
        cmd.Parameters.AddWithValue("@s", member.Status);
        cmd.Parameters.AddWithValue("@c", DateTime.UtcNow.ToString("O"));
        member.Id = (long)cmd.ExecuteScalar()!;
    }

    public void UpdateMemberRole(long memberId, string roleCode, string roleName)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE baby_member SET role_code=@rc, role_name=@rn, updated_at=@t WHERE id=@i";
        cmd.Parameters.AddWithValue("@rc", roleCode);
        cmd.Parameters.AddWithValue("@rn", roleName);
        cmd.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@i", memberId);
        cmd.ExecuteNonQuery();
    }

    public void DeleteMember(long memberId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM baby_member WHERE id=@i";
        cmd.Parameters.AddWithValue("@i", memberId);
        cmd.ExecuteNonQuery();
    }

    public void EnsureOwnerMember(long babyId, long userId, string roleCode = "father", string roleName = "爸爸")
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM baby_member WHERE baby_id=@b AND user_id=@u";
        cmd.Parameters.AddWithValue("@b", babyId);
        cmd.Parameters.AddWithValue("@u", userId);
        var count = (long)cmd.ExecuteScalar()!;
        if (count == 0)
        {
            var owner = new BabyMember
            {
                BabyId = babyId,
                UserId = userId,
                RoleCode = roleCode,
                RoleName = roleName,
                IsOwner = true,
                Status = "active",
            };
            InsertMember(owner);
        }
    }

    private static BabyMember MapMember(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        BabyId = r.GetInt64(1),
        UserId = r.GetInt64(2),
        RoleCode = r.GetString(3),
        RoleName = r.GetString(4),
        IsOwner = r.GetInt32(5) == 1,
        Status = r.GetString(6),
        CreatedAt = DateTime.Parse(r.GetString(7)),
        UpdatedAt = DateTime.Parse(r.GetString(8)),
    };
}
