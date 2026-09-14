using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChildNotes.Infrastructure.Data.Migrations;

/// <inheritdoc />
public partial class AddTaskRecordAndEmailIndexes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // task_record 复合索引（积分任务状态检查高频查询，原 (task_type,related_user_id) 前导列不匹配）。
        // CONCURRENTLY 不锁写（表增长较快），不能在事务内执行 → suppressTransaction。
        migrationBuilder.Sql(
            "CREATE INDEX CONCURRENTLY IF NOT EXISTS IX_task_record_user_id_task_type_task_key_created_at ON task_record (user_id, task_type, task_key, created_at)",
            suppressTransaction: true);

        // app_user.email 唯一索引：生产库已由 scripts/email-auth-phase2.sql 手工创建同名索引，
        // IF NOT EXISTS 保证幂等（EF 生成的 CreateIndex 在索引已存在时会失败）。
        // 唯一约束正式纳入 EF 模型，消除"新环境只跑 Migrate() 时 email 无索引无唯一约束"的漂移。
        migrationBuilder.Sql(
            "CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS IX_app_user_email ON app_user (email)",
            suppressTransaction: true);

        // refresh_token 部分索引：过渡期（旧 token 30 天有效期内）PBKDF2 慢路径回退查询
        // 需要 expires_at 过滤 + token_hash_fast IS NULL 条件，无索引时全表顺序扫描。
        // 旧 token 全部过期后（约 2026-10-15）此索引与慢路径代码一并移除。
        migrationBuilder.Sql(
            "CREATE INDEX CONCURRENTLY IF NOT EXISTS IX_refresh_token_legacy ON refresh_token (expires_at) WHERE token_hash_fast IS NULL",
            suppressTransaction: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX CONCURRENTLY IF EXISTS IX_refresh_token_legacy", suppressTransaction: true);
        migrationBuilder.Sql("DROP INDEX CONCURRENTLY IF EXISTS IX_app_user_email", suppressTransaction: true);
        migrationBuilder.Sql("DROP INDEX CONCURRENTLY IF EXISTS IX_task_record_user_id_task_type_task_key_created_at", suppressTransaction: true);
    }
}
