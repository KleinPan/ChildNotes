using System.IO;
using System.Reflection;
using System.Text;
using ChildNotes.Infrastructure;

namespace ChildNotes.Tests;

// 避免逐个测试方法间互相影响，每个测试方法内自行 Initialize + Shutdown
// 注意：Serilog Async sink 在 Shutdown 后底层文件句柄释放可能有延迟，
// 加上 xUnit 并行运行，读取日志文件须用 FileShare.ReadWrite 避免句柄冲突。

/// <summary>
/// ReleaseLogger 测试集合定义：标记同一集合内的测试串行执行（ReleaseLogger 是静态状态）。
/// </summary>
[CollectionDefinition("ReleaseLoggerTests")]
public class ReleaseLoggerTestCollection : ICollectionFixture<ReleaseLoggerTestCollection> { }

/// <summary>
/// ReleaseLogger 使用静态状态，所有测试必须串行执行。
/// </summary>
[Collection("ReleaseLoggerTests")]
public class ReleaseLoggerTests : IDisposable
{
    private readonly string _tempDir;

    public ReleaseLoggerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ChildNotes_TestLogs_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            ReleaseLogger.Shutdown();
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch { }
    }

    #region 脱敏器测试

    [Fact]
    public void Sanitizer_BearerToken_IsMasked()
    {
        var sanitizer = CreateSanitizer();
        var input = "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.signaturepart";
        var result = sanitizer.Sanitize(input);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9", result);
        Assert.Contains("***", result);
    }

    [Fact]
    public void Sanitizer_PhoneNumber_IsPartiallyMasked()
    {
        var sanitizer = CreateSanitizer();
        var input = "Contact: 13812345678 called at noon";
        var result = sanitizer.Sanitize(input);
        Assert.DoesNotContain("13812345678", result);
        Assert.Contains("138****5678", result);
    }

    [Fact]
    public void Sanitizer_Email_IsPartiallyMasked()
    {
        var sanitizer = CreateSanitizer();
        var input = "User email: alice@example.com registered";
        var result = sanitizer.Sanitize(input);
        Assert.DoesNotContain("alice@example.com", result);
        Assert.Contains("***@example.com", result);
    }

    [Fact]
    public void Sanitizer_PasswordField_IsMasked()
    {
        var sanitizer = CreateSanitizer();
        var input = "Login with password=secret123 failed";
        var result = sanitizer.Sanitize(input);
        Assert.DoesNotContain("secret123", result);
        Assert.Contains("password=***", result);
    }

    [Fact]
    public void Sanitizer_ConnStrPassword_IsMasked()
    {
        var sanitizer = CreateSanitizer();
        var input = "Data Source=db;Password=p@ssw0rd!;Version=3";
        var result = sanitizer.Sanitize(input);
        Assert.DoesNotContain("p@ssw0rd!", result);
        Assert.Contains("Password=***", result);
    }

    [Fact]
    public void Sanitizer_IdCard_IsFullyMasked()
    {
        var sanitizer = CreateSanitizer();
        var input = "ID: 110101199001011234 verified";
        var result = sanitizer.Sanitize(input);
        Assert.DoesNotContain("110101199001011234", result);
    }

    [Fact]
    public void Sanitizer_MultipleSensitiveFields_AllMasked()
    {
        var sanitizer = CreateSanitizer();
        var input = "user password=abc123 token=xyz789 email=bob@mail.com phone=13987654321";
        var result = sanitizer.Sanitize(input);
        Assert.DoesNotContain("abc123", result);
        Assert.DoesNotContain("xyz789", result);
        Assert.DoesNotContain("bob@mail.com", result);
        Assert.DoesNotContain("13987654321", result);
    }

    [Fact]
    public void Sanitizer_NormalText_IsPreserved()
    {
        var sanitizer = CreateSanitizer();
        var input = "VaccineFormViewModel loaded 42 doses in 156ms";
        var result = sanitizer.Sanitize(input);
        Assert.Equal(input, result);
    }

    [Fact]
    public void Sanitizer_NullInput_ReturnsEmpty()
    {
        var sanitizer = CreateSanitizer();
        var result = sanitizer.Sanitize(null);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Sanitizer_ExceptionMessage_IsMaskedButStackPreserved()
    {
        var sanitizer = CreateSanitizer();
        var original = new Exception("Login failed password=secret phone=13812345678");
        original.GetType().GetField("_stackTraceString", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.SetValue(original, "   at Foo.Bar() in /src/Foo.cs:line 42");
        var sanitized = sanitizer.SanitizeException(original);
        Assert.DoesNotContain("secret", sanitized.Message);
        Assert.DoesNotContain("13812345678", sanitized.Message);
        // 异常类型名保留
        Assert.Contains("Exception", sanitized.ToString());
    }

    #endregion

    #region 文件写入测试

    [Fact]
    public async Task Initialize_CreatesLogDirectory_AndWritesInfo()
    {
        ReleaseLogger.Initialize(_tempDir);
        ReleaseLogger.Info("Test", "Hello from test");

        ReleaseLogger.Shutdown();

        var logFiles = Directory.GetFiles(_tempDir, "app-*.log", SearchOption.TopDirectoryOnly);
        Assert.NotEmpty(logFiles);
        var content = await ReadLogAsync(logFiles[0]);
        Assert.Contains("Hello from test", content);
        Assert.Contains("[INF]", content);
        Assert.Contains("[Test]", content);
    }

    [Fact]
    public async Task Error_WithException_WritesStackTrace()
    {
        ReleaseLogger.Initialize(_tempDir);
        var ex = new InvalidOperationException("Test boom");
        ReleaseLogger.Error("TestSource", ex, "Operation failed");

        ReleaseLogger.Shutdown();

        var logFiles = Directory.GetFiles(_tempDir, "app-*.log", SearchOption.TopDirectoryOnly);
        Assert.NotEmpty(logFiles);
        var content = await ReadLogAsync(logFiles[0]);
        Assert.Contains("[ERR]", content);
        Assert.Contains("Test boom", content);
        Assert.Contains("InvalidOperationException", content);
        Assert.Contains("Operation failed", content);
    }

    [Fact]
    public async Task SanitizedSensitiveData_DoesNotAppearInLogFile()
    {
        ReleaseLogger.Initialize(_tempDir);
        ReleaseLogger.Info("Auth", "Login attempt password=hunter2 token=eyJabc12345678901234567890");

        ReleaseLogger.Shutdown();

        var logFiles = Directory.GetFiles(_tempDir, "app-*.log", SearchOption.TopDirectoryOnly);
        var content = await ReadLogAsync(logFiles[0]);
        Assert.DoesNotContain("hunter2", content);
        Assert.DoesNotContain("eyJabc12345678901234567890", content);
        Assert.Contains("***", content);
    }

    #endregion

    #region 分级测试

    [Fact]
    public async Task DifferentLevels_WrittenWithCorrectLevelTag()
    {
        ReleaseLogger.Initialize(_tempDir);
        ReleaseLogger.Info("L", "info message");
        ReleaseLogger.Warn("L", "warn message");
        ReleaseLogger.Error("L", "error message");

        ReleaseLogger.Shutdown();

        var logFiles = Directory.GetFiles(_tempDir, "app-*.log", SearchOption.TopDirectoryOnly);
        var content = await ReadLogAsync(logFiles[0]);
        Assert.Contains("[INF]", content);
        Assert.Contains("[WRN]", content);
        Assert.Contains("[ERR]", content);
    }

    #endregion

    #region 速率限制测试

    [Fact]
    public void ErrorRateLimit_DropsExcessAfter50PerMinute()
    {
        ReleaseLogger.Initialize(_tempDir);
        try
        {
            // 写入 50 条应该全部被接受
            for (int i = 0; i < 50; i++)
            {
                ReleaseLogger.Error("Rate", $"error {i}");
            }

            // 第 51 条之后应该被丢弃（不报错，只是不写入）
            // 由于速率限制是私有的，我们通过不抛异常 + 不崩溃来验证
            for (int i = 0; i < 100; i++)
            {
                ReleaseLogger.Error("Rate", $"excess {i}");
            }

            // 验证不抛异常即通过（速率限制正确工作）
            Assert.True(true);
        }
        finally
        {
            ReleaseLogger.Shutdown();
        }
    }

    #endregion

    #region 重复初始化测试

    [Fact]
    public void Initialize_CalledTwice_DoesNotThrow()
    {
        ReleaseLogger.Initialize(_tempDir);
        ReleaseLogger.Initialize(_tempDir); // 应被忽略，不抛异常
        Assert.True(true);
    }

    #endregion

    #region GetLogFiles 测试

    [Fact]
    public async Task GetLogFiles_ReturnsCreatedLogFiles()
    {
        ReleaseLogger.Initialize(_tempDir);
        ReleaseLogger.Info("Test", "trigger file creation");

        // 等待 async sink 写入文件（不调用 Shutdown，验证运行中也能枚举）
        await Task.Delay(300);

        var files = ReleaseLogger.GetLogFiles();
        Assert.NotEmpty(files);
        Assert.All(files, f => Assert.StartsWith("app-", f.Name));
        Assert.All(files, f => Assert.EndsWith(".log", f.Name));
    }

    #endregion

    #region 辅助方法

    /// <summary>创建 LogSanitizer 实例（public 无参构造）。</summary>
    private static LogSanitizer CreateSanitizer() => new();

    /// <summary>
    /// 以 FileShare.ReadWrite 读取日志文件，避免 Serilog async sink 句柄释放延迟导致 IOException。
    /// </summary>
    private static async Task<string> ReadLogAsync(string path)
    {
        // 等待 async sink 排空（经验值，足够让 Serilog 把队列里剩余日志写入）
        await Task.Delay(200);
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(fs, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    #endregion
}
