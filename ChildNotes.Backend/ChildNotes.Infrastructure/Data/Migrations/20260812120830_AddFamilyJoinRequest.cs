using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChildNotes.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFamilyJoinRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "family_join_request",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    baby_id = table.Column<string>(type: "text", nullable: false),
                    applicant_user_id = table.Column<string>(type: "text", nullable: false),
                    role_code = table.Column<string>(type: "text", nullable: false),
                    role_name = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "pending"),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_family_join_request", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_family_join_request_baby_id_applicant_user_id_status",
                table: "family_join_request",
                columns: new[] { "baby_id", "applicant_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_family_join_request_updated_at",
                table: "family_join_request",
                column: "updated_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "family_join_request");
        }
    }
}
