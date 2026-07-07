using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChildNotes.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "admin_account",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    username = table.Column<string>(type: "text", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    token = table.Column<string>(type: "text", nullable: true),
                    token_expire_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_login_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_account", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "admin_lottery_activity",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    cover_image = table.Column<string>(type: "text", nullable: false),
                    start_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    draw_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    cost_points = table.Column<int>(type: "integer", nullable: false),
                    winner_count = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    publish_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_lottery_activity", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "admin_lottery_prize",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    activity_id = table.Column<string>(type: "text", nullable: false),
                    prize_name = table.Column<string>(type: "text", nullable: false),
                    prize_intro = table.Column<string>(type: "text", nullable: false),
                    prize_image = table.Column<string>(type: "text", nullable: false),
                    prize_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_lottery_prize", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ai_analysis_record",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    baby_id = table.Column<string>(type: "text", nullable: false),
                    baby_name = table.Column<string>(type: "text", nullable: false),
                    range_start_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    range_end_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    source_text = table.Column<string>(type: "text", nullable: false),
                    skill_prompt = table.Column<string>(type: "text", nullable: false),
                    analysis_text = table.Column<string>(type: "text", nullable: false),
                    model = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_analysis_record", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "app_user",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    referrer_user_id = table.Column<string>(type: "text", nullable: true),
                    referrer_bound_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    nick_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    avatar_url = table.Column<string>(type: "text", nullable: false),
                    gender = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_user", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "baby",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    avatar = table.Column<string>(type: "text", nullable: false),
                    gender = table.Column<string>(type: "text", nullable: false),
                    birth_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_baby", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "baby_member",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    baby_id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    role_code = table.Column<string>(type: "text", nullable: false),
                    role_name = table.Column<string>(type: "text", nullable: false),
                    is_owner = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "active"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_baby_member", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "child_record",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    baby_id = table.Column<string>(type: "text", nullable: true),
                    record_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    record_sub_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    record_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    record_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    amount_ml = table.Column<int>(type: "integer", nullable: true),
                    duration_sec = table.Column<int>(type: "integer", nullable: true),
                    left_duration_sec = table.Column<int>(type: "integer", nullable: true),
                    right_duration_sec = table.Column<int>(type: "integer", nullable: true),
                    abnormal_flag = table.Column<bool>(type: "boolean", nullable: true),
                    temperature_value = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    height_cm = table.Column<decimal>(type: "numeric(6,2)", nullable: true),
                    weight_kg = table.Column<decimal>(type: "numeric(6,3)", nullable: true),
                    payload_json = table.Column<string>(type: "text", nullable: false),
                    deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_child_record", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ip_blacklist",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    ip_address = table.Column<string>(type: "text", nullable: false),
                    trigger_method = table.Column<string>(type: "text", nullable: true),
                    trigger_path = table.Column<string>(type: "text", nullable: true),
                    trigger_endpoint = table.Column<string>(type: "text", nullable: true),
                    request_count = table.Column<int>(type: "integer", nullable: true),
                    window_started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ip_blacklist", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lottery_activity",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    cover_image = table.Column<string>(type: "text", nullable: false),
                    start_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    draw_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    cost_points = table.Column<int>(type: "integer", nullable: false),
                    participant_count = table.Column<int>(type: "integer", nullable: false),
                    winner_count = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lottery_activity", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lottery_participation",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    activity_id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    cost_points = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lottery_participation", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lottery_prize",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    activity_id = table.Column<string>(type: "text", nullable: false),
                    prize_name = table.Column<string>(type: "text", nullable: false),
                    prize_intro = table.Column<string>(type: "text", nullable: false),
                    prize_image = table.Column<string>(type: "text", nullable: false),
                    prize_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lottery_prize", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "milestone",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    baby_id = table.Column<string>(type: "text", nullable: true),
                    title = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<string>(type: "text", nullable: true),
                    record_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    photos_json = table.Column<string>(type: "text", nullable: false, defaultValue: "[]"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_milestone", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sign_in_record",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    sign_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    sign_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    continuous_days = table.Column<int>(type: "integer", nullable: false),
                    cycle_day = table.Column<int>(type: "integer", nullable: false),
                    reward_points = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sign_in_record", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "task_record",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    task_type = table.Column<string>(type: "text", nullable: false),
                    task_key = table.Column<string>(type: "text", nullable: false),
                    related_user_id = table.Column<string>(type: "text", nullable: true),
                    points = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    payload_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_record", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_points",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false),
                    total_earned = table.Column<int>(type: "integer", nullable: false),
                    total_spent = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_points", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_admin_account_token",
                table: "admin_account",
                column: "token");

            migrationBuilder.CreateIndex(
                name: "IX_admin_account_username",
                table: "admin_account",
                column: "username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_admin_lottery_activity_status",
                table: "admin_lottery_activity",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_admin_lottery_prize_activity_id",
                table: "admin_lottery_prize",
                column: "activity_id");

            migrationBuilder.CreateIndex(
                name: "IX_ai_analysis_record_baby_id",
                table: "ai_analysis_record",
                column: "baby_id");

            migrationBuilder.CreateIndex(
                name: "IX_ai_analysis_record_user_id_baby_id_range_start_date_range_e~",
                table: "ai_analysis_record",
                columns: new[] { "user_id", "baby_id", "range_start_date", "range_end_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_app_user_referrer_user_id",
                table: "app_user",
                column: "referrer_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_app_user_username",
                table: "app_user",
                column: "username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_baby_updated_at",
                table: "baby",
                column: "updated_at");

            migrationBuilder.CreateIndex(
                name: "IX_baby_user_id",
                table: "baby",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_baby_member_baby_id_user_id",
                table: "baby_member",
                columns: new[] { "baby_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_baby_member_user_id",
                table: "baby_member",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_child_record_baby_id_record_date",
                table: "child_record",
                columns: new[] { "baby_id", "record_date" });

            migrationBuilder.CreateIndex(
                name: "IX_child_record_updated_at",
                table: "child_record",
                column: "updated_at");

            migrationBuilder.CreateIndex(
                name: "IX_child_record_user_id_record_date_record_type",
                table: "child_record",
                columns: new[] { "user_id", "record_date", "record_type" });

            migrationBuilder.CreateIndex(
                name: "IX_ip_blacklist_ip_address",
                table: "ip_blacklist",
                column: "ip_address",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lottery_activity_status",
                table: "lottery_activity",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_lottery_participation_activity_id_user_id",
                table: "lottery_participation",
                columns: new[] { "activity_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lottery_prize_activity_id",
                table: "lottery_prize",
                column: "activity_id");

            migrationBuilder.CreateIndex(
                name: "IX_milestone_baby_id",
                table: "milestone",
                column: "baby_id");

            migrationBuilder.CreateIndex(
                name: "IX_milestone_updated_at",
                table: "milestone",
                column: "updated_at");

            migrationBuilder.CreateIndex(
                name: "IX_milestone_user_id_record_date",
                table: "milestone",
                columns: new[] { "user_id", "record_date" });

            migrationBuilder.CreateIndex(
                name: "IX_sign_in_record_user_id_sign_date",
                table: "sign_in_record",
                columns: new[] { "user_id", "sign_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_task_record_task_type_related_user_id",
                table: "task_record",
                columns: new[] { "task_type", "related_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_points_user_id",
                table: "user_points",
                column: "user_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admin_account");

            migrationBuilder.DropTable(
                name: "admin_lottery_activity");

            migrationBuilder.DropTable(
                name: "admin_lottery_prize");

            migrationBuilder.DropTable(
                name: "ai_analysis_record");

            migrationBuilder.DropTable(
                name: "app_user");

            migrationBuilder.DropTable(
                name: "baby");

            migrationBuilder.DropTable(
                name: "baby_member");

            migrationBuilder.DropTable(
                name: "child_record");

            migrationBuilder.DropTable(
                name: "ip_blacklist");

            migrationBuilder.DropTable(
                name: "lottery_activity");

            migrationBuilder.DropTable(
                name: "lottery_participation");

            migrationBuilder.DropTable(
                name: "lottery_prize");

            migrationBuilder.DropTable(
                name: "milestone");

            migrationBuilder.DropTable(
                name: "sign_in_record");

            migrationBuilder.DropTable(
                name: "task_record");

            migrationBuilder.DropTable(
                name: "user_points");
        }
    }
}
