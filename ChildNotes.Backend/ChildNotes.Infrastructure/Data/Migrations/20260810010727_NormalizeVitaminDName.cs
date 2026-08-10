using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChildNotes.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// 数据归一迁移：将 supplement 记录中维D类别名统一为"维生素D3"。
    /// 别名 → 规范名：维生素D / 维D / 维D3 → 维生素D3。
    /// 处理对象：child_record.payload_json 中 record_type='supplement' 的 Name 字段。
    /// Name 可能是合并格式（如"维生素D、DHA"），按分隔符切分后逐项归一再 join。
    /// 不处理"维生素AD"/"维生素A"等不同物质（无别名映射条目）。
    /// 幂等：已是"维生素D3"的记录不会被修改。
    /// </summary>
    public partial class NormalizeVitaminDName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PL/pgSQL DO 块：遍历 supplement 记录，解析 payload_json 的 Name 字段，
            // 按分隔符切分后逐项归一别名，再 join 写回。
            // 使用 jsonb 操作避免 SQL REPLACE 误伤"维生素AD"/"维生素A"等子串。
            migrationBuilder.Sql(@"
DO $$
DECLARE
    rec RECORD;
    name_value TEXT;
    parts TEXT[];
    part TEXT;
    new_parts TEXT[] := ARRAY[]::TEXT[];
    changed BOOLEAN;
    new_name TEXT;
    new_payload JSONB;
BEGIN
    FOR rec IN
        SELECT id, payload_json::jsonb
        FROM child_record
        WHERE record_type = 'supplement' AND payload_json IS NOT NULL AND payload_json <> ''
    LOOP
        name_value := rec.payload_json->>'name';
        IF name_value IS NULL OR name_value = '' THEN
            CONTINUE;
        END IF;

        -- 按中英文顿号/逗号切分
        parts := regexp_split_to_array(name_value, '[、,，]+');
        changed := FALSE;
        new_parts := ARRAY[]::TEXT[];

        FOREACH part IN ARRAY parts LOOP
            part := btrim(part);
            IF part = '维生素D' OR part = '维D' OR part = '维D3' THEN
                new_parts := array_append(new_parts, '维生素D3');
                changed := TRUE;
            ELSE
                new_parts := array_append(new_parts, part);
            END IF;
        END LOOP;

        IF NOT changed THEN
            CONTINUE;
        END IF;

        new_name := array_to_string(new_parts, '、');
        new_payload := jsonb_set(rec.payload_json, '{name}', to_jsonb(new_name));

        UPDATE child_record
        SET payload_json = new_payload::text, updated_at = NOW()
        WHERE id = rec.id;
    END LOOP;
END $$;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 数据归一迁移不支持回滚（无法从"维生素D3"恢复原始别名）
            // Down 操作为空，保持现状。
        }
    }
}
