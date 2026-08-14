using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChildNotes.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_app_user_username",
                table: "app_user");

            migrationBuilder.DropColumn(
                name: "password_hash",
                table: "app_user");

            migrationBuilder.DropColumn(
                name: "username",
                table: "app_user");

            migrationBuilder.AddColumn<string>(
                name: "email",
                table: "app_user",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

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

            migrationBuilder.CreateIndex(
                name: "IX_app_user_email",
                table: "app_user",
                column: "email",
                unique: true);

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

            migrationBuilder.DropIndex(
                name: "IX_app_user_email",
                table: "app_user");

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
