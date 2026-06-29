namespace ChildNotes.Core.Exceptions;

public class BusinessException : Exception
{
    public int StatusCode { get; }
    public string? ErrorCode { get; }

    public BusinessException(string message, int statusCode = 400, string? errorCode = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }
}

public class UnauthorizedException : BusinessException
{
    public UnauthorizedException(string msg = "未登录") : base(msg, 401, "UNAUTHORIZED") { }
}

public class ForbiddenException : BusinessException
{
    public ForbiddenException(string msg = "无权限") : base(msg, 403, "FORBIDDEN") { }
}

public class NotFoundException : BusinessException
{
    public NotFoundException(string msg = "未找到") : base(msg, 404, "NOT_FOUND") { }
}
