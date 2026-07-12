using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChildNotes.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMembership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "membership_expire_at",
                table: "app_user",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ai_usage_record",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    usage_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    used_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_usage_record", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "membership_order",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    order_no = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    plan_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    plan_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    duration_days = table.Column<int>(type: "integer", nullable: false),
                    price_cents = table.Column<int>(type: "integer", nullable: false),
                    channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    trade_no = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    paid_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    callback_payload = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_membership_order", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_app_user_membership_expire_at",
                table: "app_user",
                column: "membership_expire_at");

            migrationBuilder.CreateIndex(
                name: "IX_ai_usage_record_user_id_usage_date",
                table: "ai_usage_record",
                columns: new[] { "user_id", "usage_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_membership_order_order_no",
                table: "membership_order",
                column: "order_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_membership_order_paid_at",
                table: "membership_order",
                column: "paid_at");

            migrationBuilder.CreateIndex(
                name: "IX_membership_order_user_id_status",
                table: "membership_order",
                columns: new[] { "user_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_usage_record");

            migrationBuilder.DropTable(
                name: "membership_order");

            migrationBuilder.DropIndex(
                name: "IX_app_user_membership_expire_at",
                table: "app_user");

            migrationBuilder.DropColumn(
                name: "membership_expire_at",
                table: "app_user");
        }
    }
}
