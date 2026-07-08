using ChildNotes.Infrastructure;
using Microsoft.Data.Sqlite;

namespace ChildNotes.Data;

public static class DbInitializer
{
    public static void Initialize(DbConnectionFactory factory)
    {
        DevLogger.Log("DB", "DbInitializer.Initialize start");
        using var conn = factory.Create();
        DevLogger.Log("DB", "DbInitializer got connection");

        conn.ExecuteNonQuery(@"
CREATE TABLE IF NOT EXISTS app_user (
    id TEXT PRIMARY KEY NOT NULL,
    username TEXT NOT NULL UNIQUE,
    password_hash TEXT NOT NULL,
    nick_name TEXT,
    avatar_url TEXT,
    gender INTEGER,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);");

        conn.ExecuteNonQuery(@"
CREATE TABLE IF NOT EXISTS baby (
    id TEXT PRIMARY KEY NOT NULL,
    user_id TEXT NOT NULL,
    name TEXT NOT NULL,
    avatar TEXT,
    gender TEXT,
    birth_date TEXT,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);");

        conn.ExecuteNonQuery(@"
CREATE TABLE IF NOT EXISTS baby_member (
    id TEXT PRIMARY KEY NOT NULL,
    baby_id TEXT NOT NULL,
    user_id TEXT NOT NULL,
    role_code TEXT NOT NULL,
    role_name TEXT NOT NULL,
    is_owner INTEGER NOT NULL DEFAULT 0,
    status TEXT NOT NULL DEFAULT 'active',
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    UNIQUE (baby_id, user_id)
);");

        conn.ExecuteNonQuery(@"
CREATE TABLE IF NOT EXISTS child_record (
    id TEXT PRIMARY KEY NOT NULL,
    user_id TEXT NOT NULL,
    baby_id TEXT,
    record_type TEXT NOT NULL,
    record_sub_type TEXT,
    record_date TEXT NOT NULL,
    record_time TEXT NOT NULL,
    amount_ml INTEGER,
    duration_sec INTEGER,
    left_duration_sec INTEGER,
    right_duration_sec INTEGER,
    abnormal_flag INTEGER,
    temperature_value REAL,
    height_cm REAL,
    weight_kg REAL,
    payload_json TEXT NOT NULL,
    deleted INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);");

        conn.ExecuteNonQuery(@"
CREATE TABLE IF NOT EXISTS milestone (
    id TEXT PRIMARY KEY NOT NULL,
    user_id TEXT NOT NULL,
    baby_id TEXT,
    title TEXT NOT NULL,
    content TEXT,
    record_date TEXT NOT NULL,
    photos_json TEXT NOT NULL DEFAULT '[]',
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);");

        conn.ExecuteNonQuery(@"
CREATE TABLE IF NOT EXISTS user_points (
    id TEXT PRIMARY KEY NOT NULL,
    user_id TEXT NOT NULL UNIQUE,
    points INTEGER NOT NULL DEFAULT 0,
    total_earned INTEGER NOT NULL DEFAULT 0,
    total_spent INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);");

        conn.ExecuteNonQuery(@"
CREATE TABLE IF NOT EXISTS sign_in_record (
    id TEXT PRIMARY KEY NOT NULL,
    user_id TEXT NOT NULL,
    sign_date TEXT NOT NULL,
    continuous_days INTEGER NOT NULL DEFAULT 1,
    reward INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL,
    UNIQUE (user_id, sign_date)
);");

        conn.ExecuteNonQuery(@"
CREATE TABLE IF NOT EXISTS task_record (
    id TEXT PRIMARY KEY NOT NULL,
    user_id TEXT NOT NULL,
    task_code TEXT NOT NULL,
    task_name TEXT NOT NULL,
    reward INTEGER NOT NULL DEFAULT 0,
    completed_at TEXT,
    created_at TEXT NOT NULL,
    UNIQUE (user_id, task_code)
);");

        conn.ExecuteNonQuery(@"
CREATE TABLE IF NOT EXISTS user_supplement_item (
    id TEXT PRIMARY KEY NOT NULL,
    user_id TEXT NOT NULL,
    type TEXT NOT NULL,
    name TEXT NOT NULL,
    created_at TEXT NOT NULL,
    UNIQUE (user_id, type, name)
);");

        conn.ExecuteNonQuery(@"
CREATE TABLE IF NOT EXISTS user_custom_vaccine (
    id TEXT PRIMARY KEY NOT NULL,
    user_id TEXT NOT NULL,
    name TEXT NOT NULL,
    created_at TEXT NOT NULL,
    UNIQUE (user_id, name)
);");

        conn.ExecuteNonQuery(@"
CREATE TABLE IF NOT EXISTS ai_analysis_record (
    id TEXT PRIMARY KEY NOT NULL,
    user_id TEXT NOT NULL,
    baby_id TEXT NOT NULL,
    baby_name TEXT,
    range_start_date TEXT NOT NULL,
    range_end_date TEXT NOT NULL,
    analysis_text TEXT NOT NULL,
    data_quality_tip TEXT,
    model TEXT,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);");

        conn.ExecuteNonQuery(@"
CREATE TABLE IF NOT EXISTS llm_config (
    id INTEGER PRIMARY KEY,
    api_base_url TEXT NOT NULL,
    api_key TEXT NOT NULL,
    model_name TEXT NOT NULL,
    temperature REAL NOT NULL DEFAULT 0.7,
    max_tokens INTEGER NOT NULL DEFAULT 2048,
    enabled INTEGER NOT NULL DEFAULT 1,
    updated_at TEXT NOT NULL
);");

        // "Ai记" 解析服务来源：local=本地 LLM（默认），server=后端解析接口
        AddColumnIfNotExists(conn, "llm_config", "note_source", "TEXT NOT NULL DEFAULT 'local'");

        conn.ExecuteNonQuery(@"
CREATE INDEX IF NOT EXISTS idx_child_record_user_date_type
    ON child_record (user_id, record_date, record_type);");

        conn.ExecuteNonQuery(@"
CREATE INDEX IF NOT EXISTS idx_child_record_baby_date
    ON child_record (baby_id, record_date);");

        conn.ExecuteNonQuery(@"
CREATE INDEX IF NOT EXISTS idx_ai_analysis_baby
    ON ai_analysis_record (baby_id, range_start_date, range_end_date);");

        // ===== 为业务表添加同步字段（增量迁移，幂等执行）=====
        // 注：SQLite 不支持 ADD COLUMN IF NOT EXISTS，需先查 PRAGMA table_info
        AddColumnIfNotExists(conn, "child_record", "is_deleted", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfNotExists(conn, "child_record", "device_id", "TEXT");
        AddColumnIfNotExists(conn, "child_record", "synced_at", "TEXT");

        AddColumnIfNotExists(conn, "milestone", "is_deleted", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfNotExists(conn, "milestone", "device_id", "TEXT");
        AddColumnIfNotExists(conn, "milestone", "synced_at", "TEXT");

        AddColumnIfNotExists(conn, "baby", "is_deleted", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfNotExists(conn, "baby", "device_id", "TEXT");
        AddColumnIfNotExists(conn, "baby", "synced_at", "TEXT");

        AddColumnIfNotExists(conn, "baby_member", "is_deleted", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfNotExists(conn, "baby_member", "device_id", "TEXT");

        AddColumnIfNotExists(conn, "ai_analysis_record", "is_deleted", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfNotExists(conn, "ai_analysis_record", "device_id", "TEXT");
        AddColumnIfNotExists(conn, "ai_analysis_record", "synced_at", "TEXT");

        // ===== 在线同步配置表 =====
        conn.ExecuteNonQuery(@"
CREATE TABLE IF NOT EXISTS sync_config (
    id INTEGER PRIMARY KEY,
    enabled INTEGER NOT NULL DEFAULT 0,
    server_url TEXT NOT NULL DEFAULT '',
    username TEXT NOT NULL DEFAULT '',
    password TEXT NOT NULL DEFAULT '',
    token TEXT NOT NULL DEFAULT '',
    last_sync_at TEXT,
    last_sync_status TEXT,
    last_sync_msg TEXT
);");
        conn.ExecuteNonQuery(@"
INSERT OR IGNORE INTO sync_config (id, enabled, server_url, username, password, token)
VALUES (1, 0, '', '', '', '');
");

        // sync_config 增量迁移：device_id 字段（用于设备级追踪与冲突归因）
        AddColumnIfNotExists(conn, "sync_config", "device_id", "TEXT");

        // ===== 同步日志表（保留最近 10 条，用于数据同步页底部展示）=====
        conn.ExecuteNonQuery(@"
CREATE TABLE IF NOT EXISTS sync_log (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    done_at TEXT NOT NULL,
    status TEXT NOT NULL,
    data_volume TEXT NOT NULL DEFAULT '',
    message TEXT NOT NULL DEFAULT ''
);");

        // 启动时清理上次未完成的 running 记录：进程被中断（崩溃/被杀）时
        // SyncTrigger 已写入 running 但未执行 UpdateFinal，残留记录会让 UI
        // 永久显示"进行中"。这里将其标记为 failed，语义与实际一致。
        conn.ExecuteNonQuery("UPDATE sync_log SET status='failed', message=COALESCE(message,'') || '（上次未完成，已重置）' WHERE status='running';");

        // child_record 增量索引：updated_at 用于增量上送查询
        conn.ExecuteNonQuery("CREATE INDEX IF NOT EXISTS idx_child_record_updated ON child_record (updated_at);");
        conn.ExecuteNonQuery("CREATE INDEX IF NOT EXISTS idx_baby_updated ON baby (updated_at);");

        // child_record 按类型查询索引：GetByType 使用 (user_id, record_type) 过滤，
        // 已有的 (user_id, record_date, record_type) 复合索引因中间列是 record_date
        // 无法高效支持仅按 user_id + record_type 的查询，故补建专用索引。
        conn.ExecuteNonQuery("CREATE INDEX IF NOT EXISTS idx_child_record_user_type ON child_record (user_id, record_type);");

        // milestone 增量索引：updated_at 用于增量上送查询
        conn.ExecuteNonQuery("CREATE INDEX IF NOT EXISTS idx_milestone_updated ON milestone (updated_at);");

        // ===== 登录会话持久化表（单行，id=1）=====
        // 用于实现关闭应用后自动登录；30 天滑动过期，每次启动续期。
        conn.ExecuteNonQuery(@"
CREATE TABLE IF NOT EXISTS user_session (
    id INTEGER PRIMARY KEY,
    user_id TEXT NOT NULL,
    issued_at TEXT NOT NULL,
    expire_at TEXT NOT NULL
);");

        // ===== 应用内消息表（轻量推送替代方案）=====
        // 用于存储后端推送下发的消息（家庭成员加入/AI 报告生成完成/运营活动等）。
        // 用户打开 App 时拉取并展示，无推送 SDK 依赖。
        conn.ExecuteNonQuery(@"
CREATE TABLE IF NOT EXISTS in_app_message (
    id TEXT PRIMARY KEY NOT NULL,
    user_id TEXT NOT NULL,
    title TEXT NOT NULL,
    body TEXT NOT NULL,
    category TEXT NOT NULL DEFAULT 'general',
    data_json TEXT NOT NULL DEFAULT '{}',
    is_read INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL,
    read_at TEXT
);");
        conn.ExecuteNonQuery(@"
CREATE INDEX IF NOT EXISTS idx_in_app_message_user_read
    ON in_app_message (user_id, is_read, created_at);");

        DevLogger.Log("DB", "DbInitializer.Initialize done");
    }

    /// <summary>
    /// 幂等地为指定表添加列。若列已存在则跳过。
    /// </summary>
    private static void AddColumnIfNotExists(SqliteConnection conn, string table, string column, string definition)
    {
        using var check = conn.CreateCommand();
        check.CommandText = $"PRAGMA table_info({table});";
        using var reader = check.ExecuteReader();
        while (reader.Read())
        {
            if (reader.GetString(1) == column) return; // 已存在，跳过
        }
        using var alter = conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        alter.ExecuteNonQuery();
        DevLogger.Log("DB", $"Migrated: {table}.{column} added");
    }
}

internal static class DbCommandExtensions
{
    public static void ExecuteNonQuery(this SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
