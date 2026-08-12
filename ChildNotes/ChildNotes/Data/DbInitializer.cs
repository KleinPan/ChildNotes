using System.Text.Json.Nodes;
using ChildNotes.Infrastructure;
using Microsoft.Data.Sqlite;

namespace ChildNotes.Data;

public static class DbInitializer
{
    /// <summary>
    /// 当前 DB schema 版本号，配合 PRAGMA user_version 使用。
    /// 新增表/列/索引时递增此版本号，已迁移到该版本的 DB 启动时跳过全部 DDL。
    /// v1→v2：新增 reminder_config 表。
    /// v2→v3：数据归一 — 维D类补充剂名称统一为"维生素D3"（child_record.payload_json + user_supplement_item.name）。
    /// v3→v4：新增 family_join_request 表（加入家庭申请/审批状态机）。
    /// </summary>
    private const int CurrentSchemaVersion = 4;

    public static void Initialize(DbConnectionFactory factory)
    {
        DevLogger.Log("DB", "DbInitializer.Initialize start");
        using var conn = factory.Create();
        DevLogger.Log("DB", "DbInitializer got connection");

        // 版本检查：已迁移到 CurrentSchemaVersion 的 DB 跳过全部 DDL（CREATE/ALTER/INDEX），
        // 避免每次启动重复执行 13 表 + 13 列 + 5 索引的 IF NOT EXISTS 探测。
        // 新库（user_version=0）或版本落后时执行完整 DDL，完成后写 user_version。
        int dbVersion = GetUserVersion(conn);
        if (dbVersion >= CurrentSchemaVersion)
        {
            DevLogger.Log("DB", $"DbInitializer skip DDL (user_version={dbVersion} >= {CurrentSchemaVersion})");
            // 仍需清理上次未完成的 sync_log running 记录（运行时数据，非 schema）
            ClearRunningSyncLog(conn);
            DevLogger.Log("DB", "DbInitializer.Initialize done (skipped DDL)");
            return;
        }

        DevLogger.Log("DB", $"DbInitializer run full DDL (user_version={dbVersion} -> {CurrentSchemaVersion})");

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
        ClearRunningSyncLog(conn);

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

        // ===== 本地提醒配置表（单行，id=1）=====
        // ReminderService 读取此表调度喂奶/睡眠提醒；用户在"提醒设置"页调整阈值。
        // 用 IF NOT EXISTS + INSERT OR IGNORE 保证幂等，已建库的老版本升级时自动补建。
        conn.ExecuteNonQuery(@"
CREATE TABLE IF NOT EXISTS reminder_config (
    id INTEGER PRIMARY KEY,
    feed_reminder_enabled INTEGER NOT NULL DEFAULT 1,
    feed_interval_hours INTEGER NOT NULL DEFAULT 3,
    sleep_reminder_enabled INTEGER NOT NULL DEFAULT 1,
    sleep_timeout_hours INTEGER NOT NULL DEFAULT 4
);");
        conn.ExecuteNonQuery(@"
INSERT OR IGNORE INTO reminder_config (id, feed_reminder_enabled, feed_interval_hours, sleep_reminder_enabled, sleep_timeout_hours)
VALUES (1, 1, 3, 1, 4);
");

        // ===== v4 加入家庭申请表 =====
        // 同步 Pull-only：客户端接收服务端下发的申请状态变化，根据状态变化生成本地 InAppMessage 通知
        conn.ExecuteNonQuery(@"
CREATE TABLE IF NOT EXISTS family_join_request (
    id TEXT PRIMARY KEY NOT NULL,
    baby_id TEXT NOT NULL,
    applicant_user_id TEXT NOT NULL,
    role_code TEXT NOT NULL,
    role_name TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'pending',
    processed_at TEXT,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);");
        conn.ExecuteNonQuery(@"
CREATE INDEX IF NOT EXISTS idx_family_join_request_updated
    ON family_join_request (updated_at);");

        // ===== v3 数据归一：维D类补充剂名称统一为"维生素D3" =====
        // 仅在从 v2 升级到 v3 时执行（新库 user_version=0 → 3 也会走一遍，新库无数据无副作用）。
        // 作用对象：
        //   1) child_record.payload_json 中 record_type='supplement' 记录的 Name 字段
        //      （Name 可能是合并格式如"维生素D、DHA"，按分隔符切分后逐项归一再 join）
        //   2) user_supplement_item.name 中 type='supplement' 的自定义项
        // 别名映射：维生素D / 维D / 维D3 → 维生素D3
        // 注意：不处理"维生素AD"/"维生素A"等不同物质（无别名映射条目）。
        NormalizeVitaminDNames(conn);

        // 全部 DDL 执行完成，写入 schema 版本号，后续启动跳过 DDL
        SetUserVersion(conn, CurrentSchemaVersion);
        DevLogger.Log("DB", $"DbInitializer.Initialize done (user_version set to {CurrentSchemaVersion})");
    }

    /// <summary>读取 PRAGMA user_version（SQLite 内置 4 字节整数，不占表空间）。</summary>
    private static int GetUserVersion(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        // Microsoft.Data.Sqlite 返回 Int64，直接 (int) 强转会抛 InvalidCastException。
        // 用 Convert.ToInt32 安全转换。
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>写入 PRAGMA user_version。</summary>
    private static void SetUserVersion(SqliteConnection conn, int version)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA user_version = {version};";
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 清理上次未完成的 sync_log running 记录：进程被中断（崩溃/被杀）时
    /// SyncTrigger 已写入 running 但未执行 UpdateFinal，残留记录会让 UI
    /// 永久显示"进行中"。这里将其标记为 failed，语义与实际一致。
    /// schema 版本检查跳过 DDL 的路径仍需执行此清理（运行时数据，非 schema）。
    /// </summary>
    private static void ClearRunningSyncLog(SqliteConnection conn)
    {
        conn.ExecuteNonQuery("UPDATE sync_log SET status='failed', message=COALESCE(message,'') || '（上次未完成，已重置）' WHERE status='running';");
    }

    /// <summary>
    /// v3 数据归一：将维D类补充剂名称统一为"维生素D3"。
    /// 别名 → 规范名：维生素D / 维D / 维D3 → 维生素D3。
    /// 处理对象：
    ///   1) child_record.payload_json（record_type='supplement'）的 Name 字段
    ///      — Name 可能是合并格式（如"维生素D、DHA"），按分隔符切分后逐项归一再 join
    ///   2) user_supplement_item.name（type='supplement'）
    /// 使用 JsonNode 解析 payload_json，避免 SQL REPLACE 误伤"维生素AD"/"维生素A"等子串。
    /// 幂等：已是"维生素D3"的记录不会被修改（归一后值与原值相同，UPDATE 写入相同值）。
    /// </summary>
    private static void NormalizeVitaminDNames(SqliteConnection conn)
    {
        // 别名 → 规范名（与 AiNoteRuleParser.SupplementAliasMap 保持一致）
        // 不含"维生素AD"/"维生素A"等不同物质
        var aliasMap = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["维生素D"] = "维生素D3",
            ["维D"] = "维生素D3",
            ["维D3"] = "维生素D3",
        };
        var separators = new[] { '、', ',', '，' };

        // 1) 归一 child_record.payload_json 中 supplement 记录的 Name 字段
        var rows = new List<(string id, string payload)>();
        using (var selectCmd = conn.CreateCommand())
        {
            selectCmd.CommandText = "SELECT id, payload_json FROM child_record WHERE record_type='supplement' AND payload_json IS NOT NULL;";
            using var r = selectCmd.ExecuteReader();
            while (r.Read())
            {
                rows.Add((r.GetString(0), r.GetString(1)));
            }
        }

        int updatedRecords = 0;
        using var updateCmd = conn.CreateCommand();
        updateCmd.CommandText = "UPDATE child_record SET payload_json=@pj, updated_at=@t WHERE id=@id;";
        var pjParam = updateCmd.Parameters.Add("@pj", SqliteType.Text);
        var tParam = updateCmd.Parameters.Add("@t", SqliteType.Text);
        var idParam = updateCmd.Parameters.Add("@id", SqliteType.Text);
        var nowUtc = DateTime.UtcNow.ToString("o");

        foreach (var (id, payload) in rows)
        {
            JsonNode? node;
            try { node = JsonNode.Parse(payload); }
            catch { continue; } // JSON 解析失败跳过，不破坏原数据
            if (node is null) continue;

            var nameNode = node["name"];
            if (nameNode is null) continue;
            var nameValue = nameNode.GetValue<string?>();
            if (string.IsNullOrEmpty(nameValue)) continue;

            // 按分隔符切分，逐项归一，再 join
            var parts = nameValue.Split(separators, StringSplitOptions.RemoveEmptyEntries)
                                 .Select(p => p.Trim())
                                 .ToList();
            bool changed = false;
            for (int i = 0; i < parts.Count; i++)
            {
                if (aliasMap.TryGetValue(parts[i], out var normalized))
                {
                    parts[i] = normalized;
                    changed = true;
                }
            }
            if (!changed) continue;

            var newName = string.Join("、", parts);
            node["name"] = newName;
            var newPayload = node.ToJsonString();

            pjParam.Value = newPayload;
            tParam.Value = nowUtc;
            idParam.Value = id;
            updateCmd.ExecuteNonQuery();
            updatedRecords++;
        }

        // 2) 归一 user_supplement_item.name（type='supplement'）
        //    name 字段是单个名称（不支持合并格式），直接精确匹配别名
        var itemRows = new List<(string id, string name)>();
        using (var selectItemCmd = conn.CreateCommand())
        {
            selectItemCmd.CommandText = "SELECT id, name FROM user_supplement_item WHERE type='supplement';";
            using var r = selectItemCmd.ExecuteReader();
            while (r.Read())
            {
                itemRows.Add((r.GetString(0), r.GetString(1)));
            }
        }

        int updatedItems = 0;
        int deletedDuplicates = 0;
        using var updateItemCmd = conn.CreateCommand();
        updateItemCmd.CommandText = "UPDATE user_supplement_item SET name=@n WHERE id=@id;";
        var nParam = updateItemCmd.Parameters.Add("@n", SqliteType.Text);
        var itemIdParam = updateItemCmd.Parameters.Add("@id", SqliteType.Text);
        using var deleteItemCmd = conn.CreateCommand();
        deleteItemCmd.CommandText = "DELETE FROM user_supplement_item WHERE id=@id;";
        var delIdParam = deleteItemCmd.Parameters.Add("@id", SqliteType.Text);

        foreach (var (id, name) in itemRows)
        {
            if (!aliasMap.TryGetValue(name, out var normalized)) continue;
            nParam.Value = normalized;
            itemIdParam.Value = id;
            try
            {
                updateItemCmd.ExecuteNonQuery();
                updatedItems++;
            }
            catch (SqliteException)
            {
                // UNIQUE 约束冲突：该用户已有"维生素D3"项，删除冗余的旧"维生素D"行
                delIdParam.Value = id;
                deleteItemCmd.ExecuteNonQuery();
                deletedDuplicates++;
            }
        }

        // 归一后可能产生重复的 user_supplement_item（如某用户原有"维生素D"和"维生素D3"两项），
        // 因 (user_id, type, name) UNIQUE 约束，UPDATE 会失败。处理策略：删除冗余行，保留已存在的"维生素D3"项。
        if (updatedRecords > 0 || updatedItems > 0 || deletedDuplicates > 0)
        {
            DevLogger.Log("DB", $"NormalizeVitaminDNames: {updatedRecords} child_record, {updatedItems} user_supplement_item updated, {deletedDuplicates} duplicates deleted");
        }
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
