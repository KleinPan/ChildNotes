-- 一次性迁移：把 baby.id 从 32 位 GUID 截短为 8 位短码
-- 执行前：备份 / 事务包裹
-- 幂等：已是 8 位的记录不受影响
BEGIN;

-- 1. 查看将要迁移的记录（可选，先 SELECT 确认）
-- SELECT id, left(id, 8) AS new_id, name FROM baby WHERE length(id) > 8;

-- 2. 冲突检测：如果截短后的新 ID 已存在（且不是源 ID），迁移会失败
-- 先检查是否有冲突：
-- SELECT b1.id AS old_id, b1.id_new, b2.id AS conflict_id
-- FROM (SELECT id, left(id, 8) AS id_new FROM baby WHERE length(id) > 8) b1
-- JOIN baby b2 ON b2.id = b1.id_new AND b2.id <> b1.id;
-- 如果上面查询有结果，需手动处理冲突后再执行下面的 UPDATE。

-- 3. 更新 baby 主键 + 所有关联表的 baby_id
UPDATE baby SET id = left(id, 8) WHERE length(id) > 8;
UPDATE baby_member SET baby_id = left(baby_id, 8) WHERE length(baby_id) > 8;
UPDATE child_record SET baby_id = left(baby_id, 8) WHERE length(baby_id) > 8;
UPDATE milestone SET baby_id = left(baby_id, 8) WHERE length(baby_id) > 8;
UPDATE ai_analysis_record SET baby_id = left(baby_id, 8) WHERE length(baby_id) > 8;

-- 4. 验证：确认没有 >8 位的 ID 残留
-- SELECT COUNT(*) AS remaining_long_ids FROM baby WHERE length(id) > 8;
-- SELECT COUNT(*) AS remaining_long_refs FROM child_record WHERE length(baby_id) > 8;

COMMIT;
