using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChildNotes.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RefactorAiUsageByType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ai_usage_record_user_id_usage_date",
                table: "ai_usage_record");

            migrationBuilder.RenameColumn(
                name: "usage_date",
                table: "ai_usage_record",
                newName: "period_start");

            migrationBuilder.AddColumn<string>(
                name: "usage_type",
                table: "ai_usage_record",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_ai_usage_record_user_id_usage_type_period_start",
                table: "ai_usage_record",
                columns: new[] { "user_id", "usage_type", "period_start" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ai_usage_record_user_id_usage_type_period_start",
                table: "ai_usage_record");

            migrationBuilder.DropColumn(
                name: "usage_type",
                table: "ai_usage_record");

            migrationBuilder.RenameColumn(
                name: "period_start",
                table: "ai_usage_record",
                newName: "usage_date");

            migrationBuilder.CreateIndex(
                name: "IX_ai_usage_record_user_id_usage_date",
                table: "ai_usage_record",
                columns: new[] { "user_id", "usage_date" },
                unique: true);
        }
    }
}
