using Microsoft.Data.Sqlite;
using ChildNotes.Models;

namespace ChildNotes.Data.Repositories;

/// <summary>
/// reminder_config 表的访问器。该表只有一行（id=1）。
/// 与 SyncConfigRepository 一致：单行配置表 + 内存缓存 + 浅拷贝防污染 + 写后失效。
/// </summary>
public sealed class ReminderConfigRepository : BaseRepository
{
    public ReminderConfigRepository(DbConnectionFactory factory) : base(factory) { }

    private const string SelectSql =
        "SELECT id, feed_reminder_enabled, feed_interval_hours, sleep_reminder_enabled, sleep_timeout_hours " +
        "FROM reminder_config WHERE id=1";

    private ReminderConfig? _cached;
    private readonly object _cacheLock = new();

    public ReminderConfig Get()
    {
        lock (_cacheLock)
        {
            if (_cached is not null) return Clone(_cached);
        }
        var cfg = QueryFirstOrDefault(SelectSql, _ => { }, Map) ?? new ReminderConfig();
        lock (_cacheLock) { _cached = cfg; }
        return Clone(cfg);
    }

    public void Save(ReminderConfig cfg)
    {
        ExecuteNonQuery(
            @"INSERT OR REPLACE INTO reminder_config
              (id, feed_reminder_enabled, feed_interval_hours, sleep_reminder_enabled, sleep_timeout_hours)
              VALUES (@id, @fre, @fih, @sre, @sth)",
            cmd => cmd
                .Add("@id", 1)
                .Add("@fre", cfg.FeedReminderEnabled ? 1 : 0)
                .Add("@fih", cfg.FeedIntervalHours)
                .Add("@sre", cfg.SleepReminderEnabled ? 1 : 0)
                .Add("@sth", cfg.SleepTimeoutHours));
        InvalidateCache();
    }

    private void InvalidateCache()
    {
        lock (_cacheLock) { _cached = null; }
    }

    private static ReminderConfig Clone(ReminderConfig c) => new()
    {
        Id = c.Id,
        FeedReminderEnabled = c.FeedReminderEnabled,
        FeedIntervalHours = c.FeedIntervalHours,
        SleepReminderEnabled = c.SleepReminderEnabled,
        SleepTimeoutHours = c.SleepTimeoutHours,
    };

    private static ReminderConfig Map(SqliteDataReader r) => new()
    {
        Id = r.GetInt32(0),
        FeedReminderEnabled = r.GetInt32(1) == 1,
        FeedIntervalHours = r.GetInt32(2),
        SleepReminderEnabled = r.GetInt32(3) == 1,
        SleepTimeoutHours = r.GetInt32(4),
    };
}
