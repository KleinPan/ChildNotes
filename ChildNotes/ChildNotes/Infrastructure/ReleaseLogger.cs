using System.Text;
using System.Text.RegularExpressions;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.Async;

namespace ChildNotes.Infrastructure;

/// <summary>
/// 基于 Serilog 的分级文件日志服务（在 Debug/Release 均生效）：
/// - 分级（Debug/Info/Warning/Error/Fatal）
/// - 文件按天滚动，自动清理 N 天前日志
/// - 异步写入（后台线程），不阻塞 UI/业务
/// - 内置脱敏器（Token / 手机号 / 密码字段 / 连接串中的凭证），避免敏感数据落盘
/// - 按级别采样限流，防止异常风暴时日志爆炸
/// 与 <see cref="DevLogger"/> 协作：DevLogger 已移除 [Conditional("DEBUG")]，
/// 在 Debug/Release 均写入内存环形缓冲区，且每次写日志时通过 ReleaseLogger.Info 落盘文件。
/// 因此 ReleaseLogger 不仅记录关键事件，也会接管所有 Info 级别日志输出到文件。
/// </summary>
public static class ReleaseLogger
{
    /// <summary>日志文件保留天数。超过则 Serilog 自动删除旧文件。</summary>
    private const int RetainedFileDays = 7;

    /// <summary>单文件最大 5MB，超过则立即滚动到新文件。</summary>
    private const long FileSizeLimitBytes = 5 * 1024 * 1024;

    /// <summary>Error/Fatal 级别每分钟最多记录 50 条，超出丢弃（防止异常风暴）。</summary>
    private const int ErrorRateLimitPerMinute = 50;

    private static ILogger _logger = Serilog.Core.Logger.None;
    private static bool _initialized;
    private static string _logDirectory = string.Empty;

    // 速率限制：简单的滑动窗口计数器（仅对 Error/Fatal）
    private static readonly Queue<long> _errorTimestamps = new();
    private static readonly object _rateLimitLock = new();

    /// <summary>脱敏器实例（线程安全）。</summary>
    private static readonly LogSanitizer _sanitizer = new();

    /// <summary>日志目录绝对路径（初始化后有效）。</summary>
    public static string LogDirectory => _logDirectory;

    /// <summary>
    /// 初始化 Serilog logger。必须在应用启动最早期调用一次（App.OnFrameworkInitializationCompleted 开头）。
    /// 重复调用会被忽略。初始化失败时回退到 NullLogger，绝不抛异常阻塞启动。
    /// </summary>
    /// <param name="logDirectory">
    /// 日志目录。null 则按平台自动选择：
    /// - Android：应用私有 files/logs（无需权限，用户不可直接访问，仅通过导出功能读出）
    /// - 桌面：MyDocuments/ChildNotes/logs
    /// </param>
    public static void Initialize(string? logDirectory = null)
    {
        if (_initialized) return;
        try
        {
            _logDirectory = logDirectory ?? GetDefaultLogDirectory();
            Directory.CreateDirectory(_logDirectory);

            var logPath = Path.Combine(_logDirectory, "app-.log");
            var levelSwitch = new LoggingLevelSwitch(LogEventLevel.Information);

            _logger = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(levelSwitch)
                .WriteTo.Async(a => a.File(
                    path: logPath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: RetainedFileDays,
                    fileSizeLimitBytes: FileSizeLimitBytes,
                    rollOnFileSizeLimit: true,
                    shared: false,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{Source}] {Message:lj}{NewLine}{Exception}",
                    encoding: Encoding.UTF8))
                .Enrich.WithProperty("Source", "App")
                .CreateLogger();

            _initialized = true;
            // 用 Serilog 自身记录初始化完成（不通过 ReleaseLogger.Log，避免脱敏器对路径的二次处理）
            _logger.Information("ReleaseLogger initialized at {Dir}", _logDirectory);
        }
        catch (Exception ex)
        {
            // 初始化失败绝不阻塞启动，回退到空 logger
            _logger = Serilog.Core.Logger.None;
            System.Diagnostics.Debug.WriteLine($"[ReleaseLogger] Init failed: {ex}");
        }
    }

    /// <summary>记录 Information 级别日志（关键生命周期事件）。</summary>
    public static void Info(string source, string message)
    {
        if (!_initialized) return;
        try
        {
            _logger.ForContext("Source", source)
                   .Information(_sanitizer.Sanitize(message));
        }
        catch { /* 日志失败不应影响业务 */ }
    }

    /// <summary>记录 Warning 级别日志（可恢复异常、降级行为）。</summary>
    public static void Warn(string source, string message)
    {
        if (!_initialized) return;
        try
        {
            _logger.ForContext("Source", source)
                   .Warning(_sanitizer.Sanitize(message));
        }
        catch { }
    }

    /// <summary>记录 Warning 级别日志（含异常）。</summary>
    public static void Warn(string source, Exception ex, string? message = null)
    {
        if (!_initialized) return;
        try
        {
            var sanitizedMsg = message is null ? null : _sanitizer.Sanitize(message);
            var sanitizedEx = _sanitizer.SanitizeException(ex);
            if (sanitizedMsg is null)
                _logger.ForContext("Source", source).Warning(sanitizedEx, "Exception occurred");
            else
                _logger.ForContext("Source", source).Warning(sanitizedEx, sanitizedMsg);
        }
        catch { }
    }

    /// <summary>记录 Error 级别日志（受速率限制，防止异常风暴）。</summary>
    public static void Error(string source, string message)
    {
        if (!_initialized) return;
        if (!AcquireErrorSlot()) return; // 速率限制
        try
        {
            _logger.ForContext("Source", source)
                   .Error(_sanitizer.Sanitize(message));
        }
        catch { }
    }

    /// <summary>记录 Error 级别日志（含异常，受速率限制）。</summary>
    public static void Error(string source, Exception ex, string? message = null)
    {
        if (!_initialized) return;
        if (!AcquireErrorSlot()) return; // 速率限制
        try
        {
            var sanitizedMsg = message is null ? null : _sanitizer.Sanitize(message);
            var sanitizedEx = _sanitizer.SanitizeException(ex);
            if (sanitizedMsg is null)
                _logger.ForContext("Source", source).Error(sanitizedEx, "Unhandled exception");
            else
                _logger.ForContext("Source", source).Error(sanitizedEx, sanitizedMsg);
        }
        catch { }
    }

    /// <summary>记录 Fatal 级别日志（应用即将崩溃）。受速率限制。</summary>
    public static void Fatal(string source, string message)
    {
        if (!_initialized) return;
        if (!AcquireErrorSlot()) return;
        try
        {
            _logger.ForContext("Source", source)
                   .Fatal(_sanitizer.Sanitize(message));
        }
        catch { }
    }

    /// <summary>记录 Fatal 级别日志（含异常）。受速率限制。</summary>
    public static void Fatal(string source, Exception ex, string? message = null)
    {
        if (!_initialized) return;
        if (!AcquireErrorSlot()) return;
        try
        {
            var sanitizedMsg = message is null ? null : _sanitizer.Sanitize(message);
            var sanitizedEx = _sanitizer.SanitizeException(ex);
            if (sanitizedMsg is null)
                _logger.ForContext("Source", source).Fatal(sanitizedEx, "Fatal: application terminating");
            else
                _logger.ForContext("Source", source).Fatal(sanitizedEx, sanitizedMsg);
        }
        catch { }
    }

    /// <summary>
    /// 刷盘并关闭 logger。应用退出时调用，确保异步队列中的日志落盘。
    /// </summary>
    public static void Shutdown()
    {
        if (!_initialized) return;
        try
        {
            // 释放私有 logger（含 async sink + file sink），确保队列中的日志落盘
            if (_logger is IDisposable disposable)
                disposable.Dispose();
        }
        catch { }
        _logger = Serilog.Core.Logger.None;
        _initialized = false;
    }

    /// <summary>
    /// 列出当前日志目录中的所有日志文件（按修改时间倒序）。
    /// 供导出功能使用。
    /// </summary>
    public static IReadOnlyList<FileInfo> GetLogFiles()
    {
        if (!_initialized || string.IsNullOrEmpty(_logDirectory) || !Directory.Exists(_logDirectory))
            return Array.Empty<FileInfo>();
        try
        {
            return new DirectoryInfo(_logDirectory)
                .GetFiles("app-*.log", SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => f.LastWriteTime)
                .ToList();
        }
        catch
        {
            return Array.Empty<FileInfo>();
        }
    }

    /// <summary>
    /// 获取当前日志目录路径。供导出服务使用。
    /// </summary>
    public static string? TryGetLogDirectory() => _initialized ? _logDirectory : null;

    #region 速率限制

    /// <summary>
    /// 滑动窗口速率限制：每分钟最多 ErrorRateLimitPerMinute 条 Error/Fatal。
    /// 超出则丢弃（返回 false）。Info/Warn 不受限。
    /// </summary>
    private static bool AcquireErrorSlot()
    {
        lock (_rateLimitLock)
        {
            var nowTicks = DateTime.UtcNow.Ticks;
            var oneMinuteAgo = nowTicks - TimeSpan.FromMinutes(1).Ticks;

            // 清理 1 分钟前的旧时间戳
            while (_errorTimestamps.Count > 0 && _errorTimestamps.Peek() < oneMinuteAgo)
                _errorTimestamps.Dequeue();

            if (_errorTimestamps.Count >= ErrorRateLimitPerMinute)
                return false; // 超限，丢弃

            _errorTimestamps.Enqueue(nowTicks);
            return true;
        }
    }

    #endregion

    #region 默认日志目录

    private static string GetDefaultLogDirectory()
    {
        if (OperatingSystem.IsAndroid())
        {
            // Android：应用私有目录 files/logs（无需权限，用户不可直接访问）
            // 通过 DependencyService 或反射获取 Android Context.GetExternalFilesDir(null)
            var dir = TryGetAndroidFilesDir();
            if (!string.IsNullOrEmpty(dir))
                return Path.Combine(dir, "logs");
            // 回退：AppContext.BaseDirectory（apk 内部，不可写，仅占位）
            return Path.Combine(AppContext.BaseDirectory, "logs");
        }

        // 桌面：MyDocuments/ChildNotes/logs（与 LogExportService 一致）
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "ChildNotes", "logs");
    }

    /// <summary>通过反射获取 Android Context.GetExternalFilesDir(null)，避免共享项目引用 Mono.Android。</summary>
    private static string? TryGetAndroidFilesDir()
    {
        try
        {
            var appType = Type.GetType("Android.App.Application, Mono.Android");
            if (appType is null) return null;
            var context = appType.GetProperty("Context")?.GetValue(null);
            if (context is null) return null;
            // GetExternalFilesDir(null) 返回 Java.IO.File
            var method = context.GetType().GetMethod("GetExternalFilesDir", new[] { typeof(string) });
            var fileObj = method?.Invoke(context, new object?[] { null });
            if (fileObj is null) return null;
            // Java.IO.File.Path
            var pathProp = fileObj.GetType().GetProperty("Path");
            return pathProp?.GetValue(fileObj) as string;
        }
        catch
        {
            return null;
        }
    }

    #endregion
}

/// <summary>
/// 日志脱敏器：在写入文件前对敏感数据进行遮蔽，防止敏感信息泄露到日志文件。
/// 线程安全（无状态，纯正则替换）。
/// </summary>
public sealed class LogSanitizer
{
    // JWT / Bearer Token（足够长的 hex/base64 段）
    private static readonly Regex TokenPattern = new(
        @"(Bearer\s+|eyJ)[A-Za-z0-9_\-\.]{20,}",
        RegexOptions.Compiled);

    // 手机号：1[3-9]xxxxxxxxx
    private static readonly Regex PhonePattern = new(
        @"1[3-9]\d{9}",
        RegexOptions.Compiled);

    // 邮箱
    private static readonly Regex EmailPattern = new(
        @"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}",
        RegexOptions.Compiled);

    // 密码 / Token / Secret / Authorization 字段值（key=value 或 key: value 形式）
    private static readonly Regex SecretFieldPattern = new(
        @"(?i)(password|passwd|pwd|token|secret|authorization|api_key|apikey|access_token|refresh_token|session|cookie)\s*[:=]\s*[""']?[^\s,""'}]+",
        RegexOptions.Compiled);

    // 连接串中的密码：Password=xxx / pwd=xxx
    private static readonly Regex ConnStrPasswordPattern = new(
        @"(?i)(password|pwd)\s*=\s*[^;]+",
        RegexOptions.Compiled);

    // 身份证号（18 位，最后一位可能是 X）
    private static readonly Regex IdCardPattern = new(
        @"\d{17}[\dXx]",
        RegexOptions.Compiled);

    /// <summary>
    /// 对纯文本消息进行脱敏。
    /// </summary>
    public string Sanitize(string? message)
    {
        if (string.IsNullOrEmpty(message)) return message ?? string.Empty;

        // 顺序：先替换高优先级（避免被低优先级部分截断）
        message = SecretFieldPattern.Replace(message, "$1=***");
        message = ConnStrPasswordPattern.Replace(message, "$1=***");
        message = TokenPattern.Replace(message, "***");
        message = IdCardPattern.Replace(message, "******************");
        message = PhonePattern.Replace(message, m => m.Value[..3] + "****" + m.Value[^4..]);
        message = EmailPattern.Replace(message, m =>
        {
            var at = m.Value.IndexOf('@');
            return at > 0 ? m.Value[..Math.Min(2, at)] + "***" + m.Value[at..] : "***";
        });
        return message;
    }

    /// <summary>
    /// 对异常进行脱敏：脱敏 Message + 全部 InnerException 的 Message。
    /// 堆栈信息不脱敏（不含用户数据，且对排查至关重要）。
    /// </summary>
    public Exception SanitizeException(Exception ex)
    {
        // 不修改原异常（防止影响其他逻辑），返回包装的脱敏异常
        var sanitizedMsg = Sanitize(ex.Message);
        var sanitizedStack = ex.StackTrace; // 堆栈不脱敏
        var sanitized = new SanitizedException(ex.GetType().Name, sanitizedMsg, sanitizedStack, ex.InnerException);

        // 递归脱敏 InnerException 链
        var current = sanitized;
        var inner = ex.InnerException;
        while (inner is not null)
        {
            current.SetInner(new SanitizedException(
                inner.GetType().Name,
                Sanitize(inner.Message),
                inner.StackTrace,
                inner.InnerException));
            current = current.WrappedInner!;
            inner = inner.InnerException;
        }
        return sanitized;
    }
}

/// <summary>
/// 脱敏后的异常包装类。保留原始异常类型名 + 脱敏后的 Message + 原始堆栈，
/// 但替换 InnerException 链为脱敏版本。
/// </summary>
public sealed class SanitizedException : Exception
{
    private readonly string _typeName;
    private readonly string? _sanitizedStackTrace;
    private SanitizedException? _wrappedInner;

    public SanitizedException(string typeName, string message, string? stackTrace, Exception? originalInner)
        : base(message, originalInner)
    {
        _typeName = typeName;
        _sanitizedStackTrace = stackTrace;
    }

    public SanitizedException? WrappedInner => _wrappedInner;

    public void SetInner(SanitizedException inner) => _wrappedInner = inner;

    public override string ToString()
    {
        // 模拟原始 Exception.ToString() 格式，便于 Serilog 输出可读
        var sb = new StringBuilder();
        sb.Append(_typeName).Append(": ").Append(Message);
        if (_sanitizedStackTrace is not null)
        {
            sb.AppendLine();
            sb.Append(_sanitizedStackTrace);
        }
        var inner = _wrappedInner;
        while (inner is not null)
        {
            sb.AppendLine();
            sb.Append(" ---> ").Append(inner._typeName).Append(": ").Append(inner.Message);
            if (inner._sanitizedStackTrace is not null)
            {
                sb.AppendLine();
                sb.Append(inner._sanitizedStackTrace);
            }
            inner = inner.WrappedInner;
        }
        return sb.ToString();
    }
}
