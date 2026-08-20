using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChildNotes.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// 空操作 migration：仅用于对齐 model snapshot，不执行任何数据库 schema 变更。
    ///
    /// 背景：b25b809 / b6af309 修改了 EmailVerificationCode.ConsumedAt / RefreshToken.RevokedAt
    /// 加 [ConcurrencyCheck] 特性，并从 ChildNotesDbContext 移除了 AppUser.Email 的 IsRequired/HasIndex().IsUnique()
    /// 配置（对应 AddEmailAuth.cs 的 Up() 把 email 列改为 nullable、不建唯一索引的生产兼容策略）。
    /// 但当时只手动改了 AddEmailAuth.cs 的 Up()，没让 EF Core 重新生成 snapshot，
    /// 导致 AddEmailAuth.Designer.cs 和 ChildNotesDbContextModelSnapshot.cs 仍保留旧的
    /// IsRequired(Email) + HasIndex(Email).IsUnique() + 无 IsConcurrencyToken() 状态。
    ///
    /// EF Core 10 启动时 db.Database.Migrate() 检测到当前 model 与 snapshot 不一致，
    /// 抛 PendingModelChangesWarning（被当 error），服务无法启动。
    ///
    /// 本 migration 的作用：
    ///   - Up()/Down() 为空操作（生产库 schema 已经是正确状态，无需变更）
    ///   - Designer.cs（本 migration 的 snapshot）和 ChildNotesDbContextModelSnapshot.cs
    ///     由 dotnet ef 工具自动重新生成，反映当前 model 的真实状态
    ///   - 这样 EF Core 启动时比对一致，不再报 PendingModelChangesWarning
    ///
    /// 注意：EF Core 自动生成的 Up() 是 DropIndex("IX_app_user_email")，但这个索引在生产库里
    /// 根本不存在（AddEmailAuth.cs 的 Up() 注释明确说"不在本 Migration 中创建"），所以必须清空。
    /// </remarks>
    public partial class FixModelSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 空操作：生产库 schema 已是正确状态，仅需要对齐 snapshot
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 空操作：Up() 无任何变更，Down() 也无操作
        }
    }
}
