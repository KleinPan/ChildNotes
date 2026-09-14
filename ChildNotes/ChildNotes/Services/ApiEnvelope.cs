using System.Text.Json;
using System.Text.Json.Serialization;
using ChildNotes.Infrastructure;

namespace ChildNotes.Services;

/// <summary>
/// 后端 {state,msg,data} 响应信封解析的共享工具。
/// AuthService 与 BaseApiClient 原各自实现了一份重复的
/// ExtractData/ExtractMessage/ReadError，收口到这里统一维护（行为与原实现一致）。
/// </summary>
internal static class ApiEnvelope
{
    /// <summary>
    /// 共享 JSON 序列化选项：后端 ASP.NET Core 默认 camelCase 序列化（serverTime/expireAt 等），
    /// 前端 DTO 用 PascalCase。开启大小写不敏感，避免字段名大小写不匹配
    /// 导致 DateTime 等类型用默认值（0001-01-01）。
    /// </summary>
    internal static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = null,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>从 {state,msg,data} 信封中提取 data 字段并反序列化（解析失败记日志并返回 default）。</summary>
    internal static T? ExtractData<T>(string json, string logSource = "Api")
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return default;
            return JsonSerializer.Deserialize<T>(data.GetRawText(), JsonOpts);
        }
        catch (Exception ex)
        {
            DevLogger.Log(logSource, "ExtractData parse fail: " + ex.Message);
            return default;
        }
    }

    /// <summary>从信封中提取 msg 字段（无 msg 或解析失败返回 null）。</summary>
    internal static string? ExtractMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("msg", out var msg) &&
                msg.ValueKind == JsonValueKind.String)
                return msg.GetString();
        }
        catch { }
        return null;
    }

    /// <summary>从错误响应的 {state,msg,code} 信封中提取 msg 和 code 字段（解析失败回退"请求失败 (状态码)"）。</summary>
    internal static async Task<(string msg, string? code)> ReadErrorAsync(
        System.Net.Http.HttpResponseMessage resp, CancellationToken ct)
    {
        try
        {
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var msg = doc.RootElement.TryGetProperty("msg", out var m) ? m.GetString() ?? "请求失败" : "请求失败";
            var code = doc.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
            return (msg, code);
        }
        catch
        {
            return ($"请求失败 ({(int)resp.StatusCode})", null);
        }
    }
}
