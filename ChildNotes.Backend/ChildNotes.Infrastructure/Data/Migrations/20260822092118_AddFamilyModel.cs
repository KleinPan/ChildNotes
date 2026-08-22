using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChildNotes.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFamilyModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "family_id",
                table: "milestone",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "family_id",
                table: "child_record",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "family_id",
                table: "baby",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "family",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_family", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "family_member",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    family_id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_family_member", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_milestone_family_id_updated_at",
                table: "milestone",
                columns: new[] { "family_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_child_record_family_id_updated_at",
                table: "child_record",
                columns: new[] { "family_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_baby_family_id",
                table: "baby",
                column: "family_id");

            migrationBuilder.CreateIndex(
                name: "IX_baby_family_id_updated_at",
                table: "baby",
                columns: new[] { "family_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_family_member_family_id_user_id",
                table: "family_member",
                columns: new[] { "family_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_family_member_single_owner",
                table: "family_member",
                column: "family_id",
                unique: true,
                filter: "role = 'owner'");

            migrationBuilder.CreateIndex(
                name: "IX_family_member_user_id",
                table: "family_member",
                column: "user_id");

            // ---- 存量数据回填：baby_member（按成员集合分组）→ Family/FamilyMember ----
            // 分组规则：owner + active 成员集合完全相同的 baby 归入同一 Family（禁止并集，防权限扩大）。
            // 签名 = 排序去重后的成员 user_id 串；整个迁移在单事务内执行，temp 表随事务结束释放。
            migrationBuilder.Sql(@"
CREATE TEMP TABLE _family_group_map ON COMMIT DROP AS
WITH baby_sig AS (
    SELECT b.id AS baby_id,
           b.user_id AS owner_id,
           COALESCE((SELECT string_agg(DISTINCT bm.user_id, ',' ORDER BY bm.user_id)
                     FROM baby_member bm
                     WHERE bm.baby_id = b.id AND bm.status = 'active'), '') AS member_sig
    FROM baby b
)
SELECT DISTINCT owner_id, member_sig FROM baby_sig;
");
            migrationBuilder.Sql(@"
ALTER TABLE _family_group_map ADD COLUMN family_id text;
UPDATE _family_group_map SET family_id = md5(random()::text || clock_timestamp()::text);
INSERT INTO family (id, name, created_at, updated_at)
SELECT family_id, '我的家庭', now(), now() FROM _family_group_map;
INSERT INTO family_member (id, family_id, user_id, role, created_at, updated_at)
SELECT md5(random()::text || clock_timestamp()::text), family_id, owner_id, 'owner', now(), now()
FROM _family_group_map;
INSERT INTO family_member (id, family_id, user_id, role, created_at, updated_at)
SELECT md5(random()::text || clock_timestamp()::text), m.family_id, u.user_id, 'member', now(), now()
FROM _family_group_map m
CROSS JOIN LATERAL unnest(string_to_array(NULLIF(m.member_sig, ''), ',')) AS u(user_id)
WHERE u.user_id <> m.owner_id
ON CONFLICT (family_id, user_id) DO NOTHING;
");
            // baby 回填：按 (owner, 签名) 重新匹配分组
            migrationBuilder.Sql(@"
UPDATE baby b SET family_id = m.family_id
FROM _family_group_map m
WHERE m.owner_id = b.user_id
  AND m.member_sig = COALESCE((SELECT string_agg(DISTINCT bm.user_id, ',' ORDER BY bm.user_id)
                               FROM baby_member bm
                               WHERE bm.baby_id = b.id AND bm.status = 'active'), '');
");
            // 子表冗余列回填（随 baby.family_id）
            migrationBuilder.Sql(@"
UPDATE child_record cr SET family_id = b.family_id
FROM baby b WHERE cr.baby_id = b.id AND b.family_id <> '';
UPDATE milestone ms SET family_id = b.family_id
FROM baby b WHERE ms.baby_id = b.id AND b.family_id <> '';
");
            // 无任何 baby 的存量用户：建默认 Family（Owner）
            migrationBuilder.Sql(@"
CREATE TEMP TABLE _orphan_users ON COMMIT DROP AS
SELECT u.id AS user_id
FROM app_user u
WHERE NOT EXISTS (SELECT 1 FROM baby b WHERE b.user_id = u.id)
  AND NOT EXISTS (SELECT 1 FROM family_member fm WHERE fm.user_id = u.id);
");
            migrationBuilder.Sql(@"
ALTER TABLE _orphan_users ADD COLUMN family_id text;
UPDATE _orphan_users SET family_id = md5(random()::text || clock_timestamp()::text);
INSERT INTO family (id, name, created_at, updated_at)
SELECT family_id, '我的家庭', now(), now() FROM _orphan_users;
INSERT INTO family_member (id, family_id, user_id, role, created_at, updated_at)
SELECT md5(random()::text || clock_timestamp()::text), family_id, user_id, 'owner', now(), now()
FROM _orphan_users;
");
            // 旧模型 Pending 加入申请全部失效（Family 化后走新流程）
            migrationBuilder.Sql(@"UPDATE family_join_request SET status = 'cancelled', updated_at = now() WHERE status = 'pending';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "family");

            migrationBuilder.DropTable(
                name: "family_member");

            migrationBuilder.DropIndex(
                name: "IX_milestone_family_id_updated_at",
                table: "milestone");

            migrationBuilder.DropIndex(
                name: "IX_child_record_family_id_updated_at",
                table: "child_record");

            migrationBuilder.DropIndex(
                name: "IX_baby_family_id",
                table: "baby");

            migrationBuilder.DropIndex(
                name: "IX_baby_family_id_updated_at",
                table: "baby");

            migrationBuilder.DropColumn(
                name: "family_id",
                table: "milestone");

            migrationBuilder.DropColumn(
                name: "family_id",
                table: "child_record");

            migrationBuilder.DropColumn(
                name: "family_id",
                table: "baby");
        }
    }
}
