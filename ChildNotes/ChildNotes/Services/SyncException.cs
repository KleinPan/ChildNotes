namespace ChildNotes.Services;

/// <summary>
/// 同步/API 调用错误分类。用于区分"网络错误 / 超时 / 鉴权 / 服务端 5xx / 业务错误"，
/// 供 <see cref="SyncPolicy"/> 决定是否重试与退避策略。
/// </summary>
public enum SyncErrorKind
{
    /// <summary>网络层错误（DNS 解析失败、连接被拒、TLS 错误等）。</summary>
    Network,

    /// <summary>请求超时（HttpClient Timeout 或服务端 408）。</summary>
    Timeout,

    /// <summary>鉴权失败（401）。需要重新登录，重试一次。</summary>
    Auth,

    /// <summary>服务端错误（5xx）。通常瞬时，可重试。</summary>
    Server5xx,

    /// <summary>业务错误（4xx 非 401）。不可重试。</summary>
    Business,

    /// <summary>未知错误。不可重试。</summary>
    Unknown,
}

/// <summary>
/// 统一的同步/API 异常。携带错误分类与可选 HTTP 状态码，
/// 供 <see cref="SyncPolicy"/> 与上层 UI 做差异化处理。
/// </summary>
public sealed class SyncException : Exception
{
    public SyncErrorKind Kind { get; }
    public int? HttpStatus { get; }

    /// <summary>是否为瞬时错误（网络/超时/5xx），可重试。</summary>
    public bool Transient => Kind is SyncErrorKind.Network
        or SyncErrorKind.Timeout
        or SyncErrorKind.Server5xx;

    public SyncException(SyncErrorKind kind, string message, int? httpStatus = null, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
        HttpStatus = httpStatus;
    }

    /// <summary>从 HttpClient 抛出的异常推断错误类型。</summary>
    public static SyncException FromHttpRequestException(HttpRequestException ex)
    {
        // HttpRequestException.StatusCode 在 .NET 5+ 可用
        var code = (int?)ex.StatusCode;
        return code switch
        {
            null => new SyncException(SyncErrorKind.Network, ex.Message, null, ex),
            >= 500 => new SyncException(SyncErrorKind.Server5xx, ex.Message, code, ex),
            401 => new SyncException(SyncErrorKind.Auth, ex.Message, code, ex),
            >= 400 => new SyncException(SyncErrorKind.Business, ex.Message, code, ex),
            _ => new SyncException(SyncErrorKind.Network, ex.Message, code, ex),
        };
    }

    /// <summary>从 HTTP 响应状态码推断错误类型。</summary>
    public static SyncException FromHttpStatus(int statusCode, string message)
        => statusCode switch
        {
            401 => new SyncException(SyncErrorKind.Auth, message, statusCode),
            >= 500 => new SyncException(SyncErrorKind.Server5xx, message, statusCode),
            >= 400 => new SyncException(SyncErrorKind.Business, message, statusCode),
            _ => new SyncException(SyncErrorKind.Unknown, message, statusCode),
        };
}
