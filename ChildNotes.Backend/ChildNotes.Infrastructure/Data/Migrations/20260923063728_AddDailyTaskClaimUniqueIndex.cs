using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChildNotes.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyTaskClaimUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 历史数据清理：历史领取记录写入时无防重约束（check-then-insert 竞态），
            // 可能存在同 (user, key, 周期) 重复行。唯一索引建在有重复数据的表上会直接失败，
            // 先删除每组 created_at 较晚的重复行（保留最早一条 = 真实首领）。
            // 注意：重复行的积分不回扣（数额小、涉及钱包流水追溯，回扣成本高于收益；
            // 产品未正式发布仅开发者数据，实际影响可忽略）。
            migrationBuilder.Sql(@"
DELETE FROM task_record t
USING task_record keep
WHERE t.task_type = 'daily_task'
  AND keep.task_type = 'daily_task'
  AND t.user_id = keep.user_id
  AND t.task_key = keep.task_key
  AND t.payload_json = keep.payload_json
  AND t.created_at > keep.created_at");

            // 领取防重唯一索引（并发双领防线）：(user_id, task_key, payload_json)
            // WHERE task_type='daily_task'。payload_json 由 ClaimTaskAsync 统一写入
            // {"date":"yyyy-MM-dd"}（daily=当日，weekly_growth=本周一日期），
            // 同周期同 key 内容相同 → 唯一约束挡住并发重复领取。
            migrationBuilder.CreateIndex(
                name: "ux_task_record_daily_claim",
                table: "task_record",
                columns: new[] { "user_id", "task_key", "payload_json" },
                unique: true,
                filter: "task_type = 'daily_task'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_task_record_daily_claim",
                table: "task_record");
        }
    }
}
