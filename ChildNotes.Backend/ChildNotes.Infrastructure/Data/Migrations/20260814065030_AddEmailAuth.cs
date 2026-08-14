using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChildNotes.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// v5 邮箱验证码认证重构 Migration（修复版）。
    ///
    /// 关键修复：原版 AddColumn(email, nullable:false, defaultValue:"") + 紧跟 CreateIndex(unique:true)
    /// 会在生产库有 ≥2 条 app_user 记录时失败（多个空字符串重复值触发 PostgreSQL 唯一约束冲突）。
    ///
    /// 修复策略：分两阶段迁移
    ///   阶段 1（本 Migration）：AddColumn(email, nullable:true) + AddColumn(email_verified_at)
    ///     旧用户 email=NULL，不强制 NOT NULL，不建唯一索引，让现有数据安全保留
    ///   阶段 2（自动化 SQL 脚本，迁移现有真实用户后执行）：
    ///     脚本位置：ChildNotes.Backend/scripts/email-auth-phase2.sql
    ///     执行流程：
    ///       a) 运维通过业务流程或手工 SQL 为现有 app_user 回填真实 email
    ///       b) 执行 scripts/email-auth-phase2.sql（幂等，含前置校验 + ALTER NOT NULL + CREATE UNIQUE INDEX）
    ///     脚本特性：
    ///       - 幂等：可重复执行（CREATE INDEX IF NOT EXISTS、ALTER 重复无副作用）
    ///       - 安全：email IS NULL 或有重复值时 DO 块抛 check_violation 拒绝继续
    ///
    /// 这样保证：
    ///   - 现有 AppUser.Id 不变（不重建表，仅 ALTER ADD COLUMN）
    ///   - 现有 BabyMember.UserId 等关系完整保留
    ///   - Migration 可以在任何用户数的生产库上无冲突应用
    /// </remarks>
    public partial class AddEmailAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 先删旧认证的索引和列（同原版）
            migrationBuilder.DropIndex(
                name: "IX_app_user_username",
                table: "app_user");

            migrationBuilder.DropColumn(
                name: "password_hash",
                table: "app_user");

            migrationBuilder.DropColumn(
                name: "username",
                table: "app_user");

            // 修复：email 列改为 nullable，不设默认值
            // 旧用户 email=NULL，新登录用户由 AuthService 写入真实邮箱
            // NOT NULL 约束 + 唯一索引由运维在回填现有用户邮箱后手动添加（见类注释）
            migrationBuilder.AddColumn<string>(
                name: "email",
                table: "app_user",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "email_verified_at",
                table: "app_user",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "email_verification_code",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    code_hash = table.Column<string>(type: "text", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_verification_code", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "refresh_token",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    device_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_token", x => x.id);
                });

            // 注意：IX_app_user_email 唯一索引不在本 Migration 中创建
            // 原因：旧用户 email=NULL，PostgreSQL 默认允许多个 NULL 共存（NULLS NOT DISTINCT 关闭）
            //       但当前数据库可能有多个旧用户，强行建唯一索引会失败
            // 唯一索引由 scripts/email-auth-phase2.sql 在回填现有用户邮箱后创建：
            //   CREATE UNIQUE INDEX IF NOT EXISTS IX_app_user_email ON app_user (email);
            // 脚本含前置校验：email IS NULL 或重复值时拒绝继续，避免运维误执行

            migrationBuilder.CreateIndex(
                name: "IX_email_verification_code_email_consumed_at",
                table: "email_verification_code",
                columns: new[] { "email", "consumed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_refresh_token_token_hash",
                table: "refresh_token",
                column: "token_hash");

            migrationBuilder.CreateIndex(
                name: "IX_refresh_token_user_id",
                table: "refresh_token",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_verification_code");

            migrationBuilder.DropTable(
                name: "refresh_token");

            migrationBuilder.DropColumn(
                name: "email",
                table: "app_user");

            migrationBuilder.DropColumn(
                name: "email_verified_at",
                table: "app_user");

            migrationBuilder.AddColumn<string>(
                name: "password_hash",
                table: "app_user",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "username",
                table: "app_user",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_app_user_username",
                table: "app_user",
                column: "username",
                unique: true);
        }
    }
}
