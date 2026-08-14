-- =====================================================================
-- 邮箱认证 v5 - 第二阶段迁移脚本（生产部署执行）
-- =====================================================================
-- 前置条件：
--   1. 已应用 EF Core Migration 20260814065030_AddEmailAuth
--      （app_user.email 列已存在，nullable=true，无唯一索引）
--   2. 已为现有 app_user 记录回填 email（手工或业务流程）
--
-- 执行方式（幂等，可重复执行）：
--   psql -h <host> -U <user> -d <db> -f email-auth-phase2.sql
--   或 docker exec -i childnotes-db psql -U postgres -d child_notes < email-auth2.sql
--
-- 校验脚本执行结果：
--   SELECT COUNT(*) FROM app_user WHERE email IS NULL;  -- 期望 0
--   SELECT COUNT(*) FROM (SELECT email FROM app_user GROUP BY email HAVING COUNT(*) > 1) d;  -- 期望 0
-- =====================================================================

BEGIN;

-- 1) 校验：还有 email IS NULL 的 app_user 记录则拒绝继续
--    防止运维忘记回填就启用 NOT NULL 约束
DO $$
DECLARE
    null_count INTEGER;
BEGIN
    SELECT COUNT(*) INTO null_count FROM app_user WHERE email IS NULL;
    IF null_count > 0 THEN
        RAISE EXCEPTION '存在 % 条 app_user.email IS NULL 的记录，请先回填邮箱后再执行本脚本', null_count
            USING ERRCODE = 'check_violation';
    END IF;
END$$;

-- 2) 校验：email 列没有重复值
--    防止回填时填错导致后续 UNIQUE 索引创建失败
DO $$
DECLARE
    dup_count INTEGER;
BEGIN
    SELECT COUNT(*) INTO dup_count
        FROM (SELECT email FROM app_user GROUP BY email HAVING COUNT(*) > 1) d;
    IF dup_count > 0 THEN
        RAISE EXCEPTION '存在 % 组重复的 email 值，请先去重再执行本脚本', dup_count
            USING ERRCODE = 'check_violation';
    END IF;
END$$;

-- 3) 设置 email NOT NULL（idempotent: ALTER 重复执行无副作用）
ALTER TABLE app_user ALTER COLUMN email SET NOT NULL;

-- 4) 创建唯一索引（idempotent: 使用 IF NOT EXISTS）
--    PostgreSQL 默认允许多个 NULL 共存，但 NOT NULL 后无 NULL，UNIQUE 生效
CREATE UNIQUE INDEX IF NOT EXISTS IX_app_user_email ON app_user (email);

COMMIT;

-- =====================================================================
-- 回滚脚本（如需）
-- =====================================================================
-- BEGIN;
-- DROP INDEX IF EXISTS IX_app_user_email;
-- ALTER TABLE app_user ALTER COLUMN email DROP NOT NULL;
-- COMMIT;
-- =====================================================================
