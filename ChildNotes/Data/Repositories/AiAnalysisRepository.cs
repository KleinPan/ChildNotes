using Microsoft.Data.Sqlite;
using ChildNotes.Models;

namespace ChildNotes.Data.Repositories;

public sealed class AiAnalysisRepository
{
    private readonly DbConnectionFactory _factory;

    public AiAnalysisRepository(DbConnectionFactory factory) => _factory = factory;

    public List<AiAnalysisRecord> GetByBaby(long babyId)
    {
        var list = new List<AiAnalysisRecord>();
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, user_id, baby_id, baby_name, range_start_date, range_end_date, analysis_text, data_quality_tip, model, created_at, updated_at FROM ai_analysis_record WHERE baby_id = @b ORDER BY created_at DESC";
        cmd.Parameters.AddWithValue("@b", babyId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(Map(r));
        return list;
    }

    public AiAnalysisRecord? FindByRange(long babyId, DateTime start, DateTime end)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, user_id, baby_id, baby_name, range_start_date, range_end_date, analysis_text, data_quality_tip, model, created_at, updated_at FROM ai_analysis_record WHERE baby_id = @b AND range_start_date = @s AND range_end_date = @e ORDER BY created_at DESC LIMIT 1";
        cmd.Parameters.AddWithValue("@b", babyId);
        cmd.Parameters.AddWithValue("@s", start.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@e", end.ToString("yyyy-MM-dd"));
        using var r = cmd.ExecuteReader();
        return r.Read() ? Map(r) : null;
    }

    public AiAnalysisRecord? FindById(long id)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, user_id, baby_id, baby_name, range_start_date, range_end_date, analysis_text, data_quality_tip, model, created_at, updated_at FROM ai_analysis_record WHERE id = @i";
        cmd.Parameters.AddWithValue("@i", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Map(r) : null;
    }

    public long Insert(AiAnalysisRecord record)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO ai_analysis_record (user_id, baby_id, baby_name, range_start_date, range_end_date, analysis_text, data_quality_tip, model, created_at, updated_at)
            VALUES (@u, @b, @bn, @s, @e, @t, @dq, @m, @c, @c); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@u", record.UserId);
        cmd.Parameters.AddWithValue("@b", record.BabyId);
        cmd.Parameters.AddWithValue("@bn", (object?)record.BabyName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@s", record.RangeStartDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@e", record.RangeEndDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@t", record.AnalysisText);
        cmd.Parameters.AddWithValue("@dq", (object?)record.DataQualityTip ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@m", (object?)record.Model ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@c", DateTime.UtcNow.ToString("O"));
        return (long)cmd.ExecuteScalar()!;
    }

    public void Delete(long id)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM ai_analysis_record WHERE id = @i";
        cmd.Parameters.AddWithValue("@i", id);
        cmd.ExecuteNonQuery();
    }

    public LlmConfig GetLlmConfig()
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT api_base_url, api_key, model_name, temperature, max_tokens, enabled FROM llm_config WHERE id = 1";
        using var r = cmd.ExecuteReader();
        if (r.Read())
        {
            return new LlmConfig
            {
                ApiBaseUrl = r.GetString(0),
                ApiKey = r.GetString(1),
                ModelName = r.GetString(2),
                Temperature = r.GetDouble(3),
                MaxTokens = r.GetInt32(4),
                Enabled = r.GetInt32(5) == 1,
            };
        }
        return new LlmConfig();
    }

    public void SaveLlmConfig(LlmConfig config)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO llm_config (id, api_base_url, api_key, model_name, temperature, max_tokens, enabled, updated_at)
            VALUES (1, @u, @k, @m, @t, @mt, @e, @now)
            ON CONFLICT(id) DO UPDATE SET api_base_url=@u, api_key=@k, model_name=@m, temperature=@t, max_tokens=@mt, enabled=@e, updated_at=@now";
        cmd.Parameters.AddWithValue("@u", config.ApiBaseUrl);
        cmd.Parameters.AddWithValue("@k", config.ApiKey);
        cmd.Parameters.AddWithValue("@m", config.ModelName);
        cmd.Parameters.AddWithValue("@t", config.Temperature);
        cmd.Parameters.AddWithValue("@mt", config.MaxTokens);
        cmd.Parameters.AddWithValue("@e", config.Enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private static AiAnalysisRecord Map(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        UserId = r.GetInt64(1),
        BabyId = r.GetInt64(2),
        BabyName = r.IsDBNull(3) ? string.Empty : r.GetString(3),
        RangeStartDate = DateTime.Parse(r.GetString(4)),
        RangeEndDate = DateTime.Parse(r.GetString(5)),
        AnalysisText = r.GetString(6),
        DataQualityTip = r.IsDBNull(7) ? string.Empty : r.GetString(7),
        Model = r.IsDBNull(8) ? string.Empty : r.GetString(8),
        CreatedAt = DateTime.Parse(r.GetString(9)),
        UpdatedAt = DateTime.Parse(r.GetString(10)),
    };
}
