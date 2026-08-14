namespace ChildNotes.Core.Config;

/// <summary>邮箱认证配置（SMTP + 验证码参数）。</summary>
public class EmailAuthOptions
{
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 465;
    public string SmtpUser { get; set; } = string.Empty;
    public string SmtpPass { get; set; } = string.Empty;
    public string FromName { get; set; } = "ChildNotes";
    public string FromAddress { get; set; } = string.Empty;

    public int CodeLength { get; set; } = 6;
    public int CodeTtlSeconds { get; set; } = 300; // 5 分钟
    public int MaxAttempts { get; set; } = 5;
    public int ResendIntervalSeconds { get; set; } = 60; // 同邮箱 60 秒限流

    public int AccessTokenExpireMinutes { get; set; } = 60; // 1 小时
    public int RefreshTokenExpireDays { get; set; } = 30; // 30 天
}
