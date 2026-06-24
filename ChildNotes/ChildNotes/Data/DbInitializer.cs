using Microsoft.Data.Sqlite;

namespace ChildNotes.Data;

public static class DbInitializer
{
    public static void Initialize(DbConnectionFactory factory)
    {
        using var conn = factory.Create();

        conn.ExecuteNonQuery(@"
CREATE TABLE IF NOT EXISTS app_user (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
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
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL,
    name TEXT NOT NULL,
    avatar TEXT,
    gender TEXT,
    birth_date TEXT,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);");

        conn.ExecuteNonQuery(@"
CREATE TABLE IF NOT EXISTS baby_member (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    baby_id INTEGER NOT NULL,
    user_id INTEGER NOT NULL,
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
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL,
    baby_id INTEGER,
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
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL,
    baby_id INTEGER,
    title TEXT NOT NULL,
    content TEXT,
    record_date TEXT NOT NULL,
    photos_json TEXT NOT NULL DEFAULT '[]',
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);");

        conn.ExecuteNonQuery(@"
CREATE TABLE IF NOT EXISTS user_points (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL UNIQUE,
    points INTEGER NOT NULL DEFAULT 0,
    total_earned INTEGER NOT NULL DEFAULT 0,
    total_spent INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);");

        conn.ExecuteNonQuery(@"
CREATE TABLE IF NOT EXISTS sign_in_record (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL,
    sign_date TEXT NOT NULL,
    continuous_days INTEGER NOT NULL DEFAULT 1,
    reward INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL,
    UNIQUE (user_id, sign_date)
);");

        conn.ExecuteNonQuery(@"
CREATE TABLE IF NOT EXISTS task_record (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL,
    task_code TEXT NOT NULL,
    task_name TEXT NOT NULL,
    reward INTEGER NOT NULL DEFAULT 0,
    completed_at TEXT,
    created_at TEXT NOT NULL,
    UNIQUE (user_id, task_code)
);");

        conn.ExecuteNonQuery(@"
CREATE TABLE IF NOT EXISTS user_supplement_item (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL,
    type TEXT NOT NULL,
    name TEXT NOT NULL,
    created_at TEXT NOT NULL,
    UNIQUE (user_id, type, name)
);");

        conn.ExecuteNonQuery(@"
CREATE TABLE IF NOT EXISTS user_custom_vaccine (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL,
    name TEXT NOT NULL,
    created_at TEXT NOT NULL,
    UNIQUE (user_id, name)
);");

        conn.ExecuteNonQuery(@"
CREATE TABLE IF NOT EXISTS ai_analysis_record (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL,
    baby_id INTEGER NOT NULL,
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

        conn.ExecuteNonQuery(@"
CREATE INDEX IF NOT EXISTS idx_child_record_user_date_type
    ON child_record (user_id, record_date, record_type);");

        conn.ExecuteNonQuery(@"
CREATE INDEX IF NOT EXISTS idx_child_record_baby_date
    ON child_record (baby_id, record_date);");

        conn.ExecuteNonQuery(@"
CREATE INDEX IF NOT EXISTS idx_ai_analysis_baby
    ON ai_analysis_record (baby_id, range_start_date, range_end_date);");
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
