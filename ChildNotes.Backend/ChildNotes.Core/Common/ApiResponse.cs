namespace ChildNotes.Core.Common;

/// <summary>
/// 统一响应格式 {state, msg, data}（与前端约定的协议契约）。
/// </summary>
public sealed class ApiResponse<T>
{
    public string State { get; set; } = "000000";
    public string Msg { get; set; } = "success";
    public T? Data { get; set; }
    /// <summary>
    /// 业务错误码（仅失败时填充，如 INSUFFICIENT_POINTS）。成功响应为 null。
    /// </summary>
    public string? Code { get; set; }

    public static ApiResponse<T> Ok(T data) => new() { State = "000000", Msg = "success", Data = data };
    public static ApiResponse<T> Fail(string msg) => new() { State = "000520", Msg = msg, Data = default };
    public static ApiResponse<T> Fail(string msg, string? code) => new() { State = "000520", Msg = msg, Data = default, Code = code };
}

public static class ApiResponse
{
    public static ApiResponse<T> Ok<T>(T data) => ApiResponse<T>.Ok(data);
    public static ApiResponse<T> Fail<T>(string msg) => ApiResponse<T>.Fail(msg);
    public static ApiResponse<T> Fail<T>(string msg, string? code) => ApiResponse<T>.Fail(msg, code);
    public static ApiResponse<object?> Ok() => new() { State = "000000", Msg = "success", Data = null };
    public static ApiResponse<object?> Fail(string msg) => new() { State = "000520", Msg = msg, Data = null };
    public static ApiResponse<object?> Fail(string msg, string? code) => new() { State = "000520", Msg = msg, Data = null, Code = code };
}
