using Microsoft.Data.Sqlite;
using ChildNotes.Models;

namespace ChildNotes.Data.Repositories;

public sealed class AiAnalysisRepository : BaseRepository
{
    public AiAnalysisRepository(DbConnectionFactory factory) : base(factory) { }

    private const string SelectBase =
        "SELECT id, user_id, baby_id, baby_name, range_start_date, range_end_date, analysis_text, data_quality_tip, model, created_at, updated_at FROM ai_analysis_record";

    public List<AiAnalysisRecord> GetByBaby(long babyId)
        => Query(SelectBase + " WHERE baby_id = @b ORDER BY created_at DESC",
            cmd => cmd.Add("@b", babyId), Map);

    public AiAnalysisRecord? FindByRange(long babyId, DateTime start, DateTime end)
        => QueryFirstOrDefault(
            SelectBase + " WHERE baby_id = @b AND range_start_date = @s AND range_end_date = @e ORDER BY created_at DESC LIMIT 1",
            cmd => cmd.Add("@b", babyId).AddDate("@s", start).AddDate("@e", end),
            Map);

    public AiAnalysisRecord? FindById(long id)
        => QueryFirstOrDefault(SelectBase + " WHERE id = @i", cmd => cmd.Add("@i", id), Map);

    public long Insert(AiAnalysisRecord record)
        => (long)ExecuteScalar(
            @"INSERT INTO ai_analysis_record (user_id, baby_id, baby_name, range_start_date, range_end_date, analysis_text, data_quality_tip, model, created_at, updated_at)
              VALUES (@u, @b, @bn, @s, @e, @t, @dq, @m, @c, @c); SELECT last_insert_rowid();",
            cmd => cmd
                .Add("@u", record.UserId)
                .Add("@b", record.BabyId)
                .Add("@bn", (object?)record.BabyName ?? DBNull.Value)
                .AddDate("@s", record.RangeStartDate)
                .AddDate("@e", record.RangeEndDate)
                .Add("@t", record.AnalysisText)
                .Add("@dq", (object?)record.DataQualityTip ?? DBNull.Value)
                .Add("@m", (object?)record.Model ?? DBNull.Value)
                .AddUtc("@c", DateTime.UtcNow))!;

    public void Delete(long id)
        => ExecuteNonQuery("DELETE FROM ai_analysis_record WHERE id = @i", cmd => cmd.Add("@i", id));

    /// <summary>LlmConfig 内存缓存：单行配置表，仅在 SaveLlmConfig 后失效。</summary>
    private LlmConfig? _llmCached;
    private readonly object _llmCacheLock = new();

    public LlmConfig GetLlmConfig()
    {
        lock (_llmCacheLock)
        {
            if (_llmCached is not null) return _llmCached;
        }
        LlmConfig cfg;
        using (var conn = OpenConnection())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT api_base_url, api_key, model_name, temperature, max_tokens, enabled, note_source FROM llm_config WHERE id = 1";
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                cfg = new LlmConfig
                {
                    ApiBaseUrl = r.GetString(0),
                    ApiKey = r.GetString(1),
                    ModelName = r.GetString(2),
                    Temperature = r.GetDouble(3),
                    MaxTokens = r.GetInt32(4),
                    Enabled = r.GetInt32(5) == 1,
                    NoteSource = r.IsDBNull(6) ? "local" : r.GetString(6),
                };
            }
            else
            {
                cfg = new LlmConfig();
            }
        }
        lock (_llmCacheLock) { _llmCached = cfg; }
        return cfg;
    }

    public void SaveLlmConfig(LlmConfig config)
    {
        ExecuteNonQuery(
            @"INSERT INTO llm_config (id, api_base_url, api_key, model_name, temperature, max_tokens, enabled, note_source, updated_at)
              VALUES (1, @u, @k, @m, @t, @mt, @e, @ns, @now)
              ON CONFLICT(id) DO UPDATE SET api_base_url=@u, api_key=@k, model_name=@m, temperature=@t, max_tokens=@mt, enabled=@e, note_source=@ns, updated_at=@now",
            cmd => cmd
                .Add("@u", config.ApiBaseUrl)
                .Add("@k", config.ApiKey)
                .Add("@m", config.ModelName)
                .Add("@t", config.Temperature)
                .Add("@mt", config.MaxTokens)
                .Add("@e", config.Enabled ? 1 : 0)
                .Add("@ns", string.IsNullOrEmpty(config.NoteSource) ? "local" : config.NoteSource)
                .AddUtc("@now", DateTime.UtcNow));
        lock (_llmCacheLock) { _llmCached = null; }
    }

    private static AiAnalysisRecord Map(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        UserId = r.GetInt64(1),
        BabyId = r.GetInt64(2),
        BabyName = r.IsDBNull(3) ? string.Empty : r.GetString(3),
        RangeStartDate = DateTimeExtensions.ParseDb(r.GetString(4)),
        RangeEndDate = DateTimeExtensions.ParseDb(r.GetString(5)),
        AnalysisText = r.GetString(6),
        DataQualityTip = r.IsDBNull(7) ? string.Empty : r.GetString(7),
        Model = r.IsDBNull(8) ? string.Empty : r.GetString(8),
        CreatedAt = DateTimeExtensions.ParseDb(r.GetString(9)),
        UpdatedAt = DateTimeExtensions.ParseDb(r.GetString(10)),
    };
}
